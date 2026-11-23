"""python export_policy.py runs/mine/latest.pt   -> Unity project's Assets/StreamingAssets/policy.bytes
Writes the actor MLP (body layers + policy head; the value head is training-only) as a tiny binary the C# MlpPolicy reads."""
import argparse, os, struct
import numpy as np
import torch

DEFAULT_OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Battle of Hexagrad",
                           "Assets", "StreamingAssets", "policy.bytes")


def export(ckpt, out):
    sd = torch.load(ckpt, map_location="cpu")["model"]
    layers = [(sd[f"body.{i}.weight"], sd[f"body.{i}.bias"]) for i in (0, 2, 4)] + [(sd["pi.weight"], sd["pi.bias"])]
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "wb") as f:
        f.write(struct.pack("<ii", 0x31575848, len(layers)))                      # "HXW1", layer count
        for w, b in layers:
            f.write(struct.pack("<ii", w.shape[1], w.shape[0]))                     # in, out
            f.write(w.numpy().astype("<f4").tobytes()); f.write(b.numpy().astype("<f4").tobytes())
    return os.path.getsize(out)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("ckpt"); ap.add_argument("--out", default=DEFAULT_OUT)
    a = ap.parse_args()
    n = export(a.ckpt, os.path.abspath(a.out))
    print(f"wrote {os.path.abspath(a.out)} ({n/1e6:.1f} MB)")
