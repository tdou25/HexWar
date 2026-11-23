"""python evaluate.py runs/hex/latest.pt --games 200   -> win/draw/loss vs the (default, non-randomised) scripted rusher."""
import argparse
import numpy as np
import torch

from hexwar import HexWarVec, ActorCritic
from hexwar.env import SCRIPTED


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("ckpt", nargs="?", default=None, help="checkpoint; omit to evaluate scripted-vs-scripted")
    ap.add_argument("--games", type=int, default=200)
    ap.add_argument("--envs", type=int, default=50)
    ap.add_argument("--sample", action="store_true", help="sample actions instead of greedy")
    ap.add_argument("--seed", type=int, default=12345)
    args = ap.parse_args()

    net = None
    if args.ckpt:
        ck = torch.load(args.ckpt, map_location="cpu")
        net = ActorCritic(ck.get("hidden", 512)); net.load_state_dict(ck["model"]); net.eval()

    env = HexWarVec(args.envs, seed=args.seed, randomize_scripted=False)
    obs, mask = env.initial()
    seat = np.arange(args.envs) % 2                  # agent plays grey in even games, red in odd
    res = {"win": 0, "draw": 0, "loss": 0}; hours = []; done_games = 0
    while done_games < args.games:
        actions = np.full((args.envs, 2), SCRIPTED, dtype=np.int32)
        if net is not None:
            idx = np.arange(args.envs)
            a, _, _ = net.act(torch.as_tensor(obs[idx, seat]), torch.as_tensor(mask[idx, seat]), greedy=not args.sample)
            actions[idx, seat] = a.numpy()
        obs, mask, rew, done, info = env.step(actions)
        for i in np.flatnonzero(done):
            if done_games >= args.games: break
            w = info["winner"][i]
            res["draw" if w == 2 else ("win" if w == seat[i] else "loss")] += 1
            hours.append(info["hours"][i]); done_games += 1
            seat[i] = 1 - seat[i]
    print(f"{'agent' if net else 'scripted(grey)'} vs scripted over {done_games} games: {res}  mean length {np.mean(hours)/24:.1f} days")
    env.close()


if __name__ == "__main__":
    main()
