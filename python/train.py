"""PPO with opponent mixing for HexWar.   python train.py --run mine --envs 32 --total-steps 20000000

Every episode, each game samples an opponent for the learner:
  self   - both seats are the current policy (both seats train)
  pool   - the learner faces a frozen older snapshot
  script - the learner faces the built-in rusher (parameters randomised per game by the server)
The learner takes a random seat (grey or red) in every game; the 180-degree observation flip means one network plays both.
"""
import argparse, collections, copy, os, time
import numpy as np
import torch
import torch.nn.functional as F

from hexwar import HexWarVec, ActorCritic
from hexwar.env import SCRIPTED, N_ACTIONS
from hexwar.plotting import plot_training

SELF, POOL, SCRIPT = 0, 1, 2


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--run", default="hex")
    ap.add_argument("--envs", type=int, default=32)
    ap.add_argument("--rollout", type=int, default=128)
    ap.add_argument("--total-steps", type=int, default=20_000_000, help="env steps (game hours), both seats counted")
    ap.add_argument("--lr", type=float, default=3e-4)
    ap.add_argument("--gamma", type=float, default=0.997)
    ap.add_argument("--lam", type=float, default=0.95)
    ap.add_argument("--clip", type=float, default=0.2)
    ap.add_argument("--ent", type=float, default=0.01)
    ap.add_argument("--epochs", type=int, default=3)
    ap.add_argument("--minibatch", type=int, default=2048)
    ap.add_argument("--reward-scale", type=float, default=0.01)
    ap.add_argument("--p-self", type=float, default=0.35)
    ap.add_argument("--p-pool", type=float, default=0.25)     # remainder = scripted
    ap.add_argument("--snap-every", type=int, default=25, help="iterations between pool snapshots")
    ap.add_argument("--pool-size", type=int, default=12)
    ap.add_argument("--device", default="cpu")
    ap.add_argument("--hidden", type=int, default=512)
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--resume", default=None)
    args = ap.parse_args()

    torch.manual_seed(args.seed); rng = np.random.default_rng(args.seed)
    dev = torch.device(args.device)
    out = os.path.join("runs", args.run); os.makedirs(out, exist_ok=True)

    net = ActorCritic(args.hidden).to(dev)
    if args.resume:
        net.load_state_dict(torch.load(args.resume, map_location=dev)["model"])
    opt = torch.optim.Adam(net.parameters(), lr=args.lr, eps=1e-5)
    pool = []                                   # frozen snapshots

    N, T = args.envs, args.rollout
    env = HexWarVec(N, seed=args.seed)
    obs, mask = env.initial()

    kind = np.zeros(N, int); seat = np.zeros(N, int); opp_idx = np.zeros(N, int)

    def assign(i):
        r = rng.random()
        k = SELF if r < args.p_self else (POOL if r < args.p_self + args.p_pool else SCRIPT)
        if k == POOL and not pool: k = SELF
        kind[i] = k; seat[i] = rng.integers(2); opp_idx[i] = rng.integers(len(pool)) if pool else 0

    for i in range(N): assign(i)

    def slot_roles():
        """learn[N,2] bool, snap[N,2] int (-1 = none), script[N,2] bool"""
        learn = np.zeros((N, 2), bool); snap = -np.ones((N, 2), int); script = np.zeros((N, 2), bool)
        for i in range(N):
            if kind[i] == SELF: learn[i] = True
            else:
                learn[i, seat[i]] = True
                if kind[i] == POOL: snap[i, 1 - seat[i]] = opp_idx[i]
                else: script[i, 1 - seat[i]] = True
        return learn, snap, script

    S = 2 * N
    B_obs = torch.zeros(T, S, obs.shape[-1], device=dev); B_mask = torch.zeros(T, S, N_ACTIONS, dtype=torch.bool, device=dev)
    B_act = torch.zeros(T, S, dtype=torch.long, device=dev); B_logp = torch.zeros(T, S, device=dev)
    B_val = torch.zeros(T, S, device=dev); B_rew = torch.zeros(T, S, device=dev)
    B_done = torch.zeros(T, S, device=dev); B_learn = torch.zeros(T, S, dtype=torch.bool, device=dev)

    stats = {k: collections.deque(maxlen=200) for k in ("script", "pool", "self")}
    lens = collections.deque(maxlen=200)
    it = 0; t0 = time.time(); ret_acc = np.zeros(N)
    n_iters = args.total_steps // (T * N)
    ep_ret = np.zeros((N, 2))                  # running raw (unscaled) return per seat
    episodes = []                              # (timestep, return, opponent kind) per finished learner episode
    metrics = []

    def save_logs(final=False):
        import csv
        with open(os.path.join(out, 'episodes.csv'), 'w', newline='') as f:
            w = csv.writer(f); w.writerow(['steps', 'return', 'kind']); w.writerows(episodes)
        with open(os.path.join(out, 'metrics.csv'), 'w', newline='') as f:
            w = csv.writer(f); w.writerow(['steps', 'winrate_script', 'winrate_pool', 'ep_len_days'])
            for m in metrics: w.writerow([m['steps'], m['winrate_script'], m['winrate_pool'], m['ep_len_days']])
        plot_training(episodes, metrics, os.path.join(out, 'training_curve.png'))

    while it < n_iters:
        for g in opt.param_groups: g["lr"] = args.lr * (1 - it / n_iters)
        learn, snap, script = slot_roles()
        for t in range(T):
            o = torch.as_tensor(obs.reshape(S, -1), device=dev); m = torch.as_tensor(mask.reshape(S, -1), device=dev)
            a, lp, v = net.act(o, m)
            actions = a.cpu().numpy().reshape(N, 2).copy()
            # frozen snapshot slots
            for j in np.unique(snap[snap >= 0]):
                sel = snap == j; idx = np.flatnonzero(sel.reshape(-1))
                sa, _, _ = pool[j].act(o[idx], m[idx]); actions[sel] = sa.cpu().numpy()
            actions[script] = SCRIPTED
            B_obs[t] = o; B_mask[t] = m; B_act[t] = a; B_logp[t] = lp; B_val[t] = v
            B_learn[t] = torch.as_tensor(learn.reshape(-1), device=dev)

            obs, mask, rew, done, info = env.step(actions)
            B_rew[t] = torch.as_tensor(rew.reshape(-1) * args.reward_scale, device=dev)
            ep_ret += rew
            B_done[t] = torch.as_tensor(np.repeat(done, 2).astype(np.float32), device=dev)

            for i in np.flatnonzero(done):
                w = info["winner"][i]; lens.append(info["hours"][i])
                name = ("self", "pool", "script")[kind[i]]
                ts = it * T * N + (t + 1) * N
                for sd in range(2):
                    if learn[i, sd]: episodes.append((ts, float(ep_ret[i, sd]), name))
                ep_ret[i] = 0
                if kind[i] != SELF:
                    stats[name].append(1.0 if w == seat[i] else (0.5 if w == 2 else 0.0))
                assign(i)
            if done.any(): learn, snap, script = slot_roles()

        with torch.no_grad():
            _, last_v = net(torch.as_tensor(obs.reshape(S, -1), device=dev), torch.as_tensor(mask.reshape(S, -1), device=dev))
        adv = torch.zeros(T, S, device=dev); lastgae = torch.zeros(S, device=dev)
        for t in reversed(range(T)):
            nv = last_v if t == T - 1 else B_val[t + 1]
            nd = 1.0 - B_done[t]
            delta = B_rew[t] + args.gamma * nv * nd - B_val[t]
            lastgae = delta + args.gamma * args.lam * nd * lastgae
            adv[t] = lastgae
        ret = adv + B_val

        sel = B_learn.reshape(-1)
        f_obs = B_obs.reshape(T * S, -1)[sel]; f_mask = B_mask.reshape(T * S, -1)[sel]
        f_act = B_act.reshape(-1)[sel]; f_logp = B_logp.reshape(-1)[sel]
        f_adv = adv.reshape(-1)[sel]; f_ret = ret.reshape(-1)[sel]
        f_adv = (f_adv - f_adv.mean()) / (f_adv.std() + 1e-8)
        n = f_obs.shape[0]
        for _ in range(args.epochs):
            perm = torch.randperm(n, device=dev)
            for s in range(0, n, args.minibatch):
                b = perm[s:s + args.minibatch]
                dist, v = net(f_obs[b], f_mask[b])
                lp = dist.log_prob(f_act[b]); ratio = (lp - f_logp[b]).exp()
                pg = -torch.min(ratio * f_adv[b], ratio.clamp(1 - args.clip, 1 + args.clip) * f_adv[b]).mean()
                vl = F.mse_loss(v, f_ret[b]); ent = dist.entropy().mean()
                loss = pg + 0.5 * vl - args.ent * ent
                opt.zero_grad(); loss.backward(); torch.nn.utils.clip_grad_norm_(net.parameters(), 0.5); opt.step()

        it += 1
        if it % args.snap_every == 0:
            snap_net = copy.deepcopy(net).eval()
            for p in snap_net.parameters(): p.requires_grad_(False)
            pool.append(snap_net); pool = pool[-args.pool_size:]
        if it % 5 == 0 or it == n_iters:
            torch.save({"model": net.state_dict(), "hidden": args.hidden}, os.path.join(out, "latest.pt"))
        if it % 5 == 0:
            sps = it * T * N / (time.time() - t0)
            wr = {k: (f"{np.mean(v):.2f}({len(v)})" if v else "-") for k, v in stats.items()}
            metrics.append({'steps': it * T * N, 'winrate_script': float(np.mean(stats['script'])) if stats['script'] else '',
                            'winrate_pool': float(np.mean(stats['pool'])) if stats['pool'] else '',
                            'ep_len_days': float(np.mean(lens)) / 24 if lens else 0.0})
            print(f"it {it}/{n_iters}  steps {it*T*N:,}  {sps:,.0f} steps/s  winrate vs script {wr['script']}  vs pool {wr['pool']}  "
                  f"ep len {np.mean(lens)/24 if lens else 0:.1f}d  ent {ent.item():.2f} vl {vl.item():.3f}", flush=True)
            if it % 50 == 0: save_logs()
    save_logs(final=True)
    print('saved', os.path.join(out, 'training_curve.png'))
    env.close()


if __name__ == "__main__":
    main()
