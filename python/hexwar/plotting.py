import csv
import numpy as np
import matplotlib
matplotlib.use("Agg")           # headless: just save PNGs
import matplotlib.pyplot as plt


def _rolling(y, w):
    if len(y) == 0: return y
    w = max(1, min(w, len(y)))
    c = np.cumsum(np.insert(y, 0, 0.0))
    out = (c[w:] - c[:-w]) / w
    return np.concatenate([np.full(w - 1, np.nan), out])


def plot_training(episodes, metrics, path):
    """episodes: list of (timestep, return, kind) for every finished learner episode (raw, unscaled game reward).
    metrics: list of dict rows with steps / winrate_script / winrate_pool."""
    fig, ax = plt.subplots(2, 1, figsize=(10, 8), sharex=True)
    if episodes:
        x = np.array([e[0] for e in episodes], float); y = np.array([e[1] for e in episodes], float)
        ax[0].scatter(x, y, s=3, alpha=0.12, color="tab:blue", label="episode return")
        ax[0].plot(x, _rolling(y, 200), color="tab:red", lw=2, label="rolling mean (200 episodes)")
        for k, c in (("script", "tab:green"), ("self", "tab:purple"), ("pool", "tab:orange")):
            sel = np.array([e[2] == k for e in episodes])
            if sel.sum() > 20: ax[0].plot(x[sel], _rolling(y[sel], 100), color=c, lw=1.2, alpha=0.9, label=f"mean vs {k}")
    ax[0].axhline(0, color="gray", lw=0.6)
    ax[0].set_ylabel("learner episode return\n(win +1000, kill +25, outpost +100, ...)")
    ax[0].legend(loc="best", fontsize=8); ax[0].set_title("HexWar PPO training")
    if metrics:
        s = [m["steps"] for m in metrics]
        for key, c in (("winrate_script", "tab:green"), ("winrate_pool", "tab:orange")):
            v = [m[key] for m in metrics]
            ax[1].plot(s, v, color=c, label=key.replace("winrate_", "win rate vs "))
    ax[1].axhline(0.5, color="gray", lw=0.6, ls="--"); ax[1].set_ylim(-0.02, 1.02)
    ax[1].set_ylabel("win rate (draw = 0.5)"); ax[1].set_xlabel("timesteps (game hours)")
    ax[1].legend(loc="best", fontsize=8)
    fig.tight_layout(); fig.savefig(path, dpi=130); plt.close(fig)


def plot_from_csv(run_dir):
    import os
    eps = []
    with open(os.path.join(run_dir, "episodes.csv")) as f:
        for r in csv.DictReader(f): eps.append((float(r["steps"]), float(r["return"]), r["kind"]))
    met = []
    with open(os.path.join(run_dir, "metrics.csv")) as f:
        for r in csv.DictReader(f):
            met.append({"steps": float(r["steps"]),
                        "winrate_script": float(r["winrate_script"] or "nan"),
                        "winrate_pool": float(r["winrate_pool"] or "nan")})
    plot_training(eps, met, os.path.join(run_dir, "training_curve.png"))
