"""Vectorised HexWar environment backed by the C# game logic (Tools/Server), one source of truth with Unity."""
import os
import subprocess
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SERVER_DIR = os.path.join(ROOT, "Tools", "Server")
SERVER_DLL = os.path.join(SERVER_DIR, "bin", "Release", "net6.0", "Server.dll")

OBS_SIZE = 1635
N_ACTIONS = 99
SCRIPTED = -1          # pass as an action to let the built-in scripted bot play that slot
WINNER_NONE = 255


def build_server():
    subprocess.check_call(["dotnet", "build", "-c", "Release", "-nologo", "-v", "q"], cwd=SERVER_DIR)


class HexWarVec:
    """N games, 2 slots each (team 0 = grey, team 1 = red), every slot observed from its own perspective.

    step() takes int actions [N,2] (use SCRIPTED = -1 for the built-in bot) and returns
      obs [N,2,1635] float32, mask [N,2,99] bool, reward [N,2] float32 (raw game reward, zero-sum),
      done [N] bool, info dict(winner [N], hours [N]).
    Finished games are reset automatically; obs/mask then already describe the next game.
    """

    def __init__(self, n_envs=32, seed=1, randomize_scripted=True, rebuild=False):
        if rebuild or not os.path.exists(SERVER_DLL):
            build_server()
        self.n = n_envs
        self.proc = subprocess.Popen(
            ["dotnet", SERVER_DLL, "--envs", str(n_envs), "--seed", str(seed),
             "--randomize", "1" if randomize_scripted else "0"],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, bufsize=1 << 20)
        n, d, a = np.frombuffer(self._read(12), dtype=np.int32)
        assert (n, d, a) == (n_envs, OBS_SIZE, N_ACTIONS), f"server/python mismatch: {(n, d, a)}"
        self._last = self._read_state()

    def _read(self, nbytes):
        buf = self.proc.stdout.read(nbytes)
        if len(buf) != nbytes:
            raise RuntimeError("HexWar server died")
        return buf

    def _read_state(self):
        n = self.n
        obs = np.frombuffer(self._read(n * 2 * OBS_SIZE * 4), dtype=np.float32).reshape(n, 2, OBS_SIZE)
        mask = np.frombuffer(self._read(n * 2 * N_ACTIONS), dtype=np.uint8).reshape(n, 2, N_ACTIONS).astype(bool)
        rew = np.frombuffer(self._read(n * 2 * 4), dtype=np.float32).reshape(n, 2)
        done = np.frombuffer(self._read(n), dtype=np.uint8).astype(bool)
        winner = np.frombuffer(self._read(n), dtype=np.uint8)
        hours = np.frombuffer(self._read(n * 4), dtype=np.int32)
        return obs.copy(), mask, rew.copy(), done, {"winner": winner.copy(), "hours": hours.copy()}

    def reset(self):
        self.proc.stdin.write(np.int32(1).tobytes()); self.proc.stdin.flush()
        obs, mask, *_ = self._read_state()
        return obs, mask

    def initial(self):
        obs, mask, *_ = self._last
        return obs, mask

    def step(self, actions):
        actions = np.ascontiguousarray(actions, dtype=np.int32).reshape(self.n, 2)
        self.proc.stdin.write(np.int32(0).tobytes() + actions.tobytes()); self.proc.stdin.flush()
        return self._read_state()

    def close(self):
        try:
            self.proc.stdin.write(np.int32(2).tobytes()); self.proc.stdin.flush()
        except Exception:
            pass
        self.proc.kill()

    def __enter__(self): return self
    def __exit__(self, *a): self.close()
