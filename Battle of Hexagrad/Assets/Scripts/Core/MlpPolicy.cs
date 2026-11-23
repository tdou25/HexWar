using System;
using System.IO;

namespace HexWar
{
    /// <summary>
    /// Pure-C# inference for the PyTorch policy trained in python/ (an MLP with ReLU between layers; the last
    /// layer is the 99 action logits). Weights come from python/export_policy.py ("HXW1" binary).
    /// Invalid actions are masked with GameSim.IsValidAction, exactly as during training.
    /// </summary>
    public sealed class MlpPolicy
    {
        readonly int[] inDim, outDim;
        readonly float[][] w, b;
        public bool Greedy = true;                 // false = sample from the softmax
        readonly float[] obs = new float[Obs.Size];
        Random rng = new Random(1234);

        MlpPolicy(int layers) { inDim = new int[layers]; outDim = new int[layers]; w = new float[layers][]; b = new float[layers][]; }

        public static MlpPolicy Load(byte[] data)
        {
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                if (r.ReadInt32() != 0x31575848) throw new InvalidDataException("not an HXW1 policy file");   // "HXW1"
                int n = r.ReadInt32();
                var p = new MlpPolicy(n);
                for (int l = 0; l < n; l++)
                {
                    p.inDim[l] = r.ReadInt32(); p.outDim[l] = r.ReadInt32();
                    p.w[l] = ReadFloats(r, p.inDim[l] * p.outDim[l]); p.b[l] = ReadFloats(r, p.outDim[l]);
                }
                if (p.inDim[0] != Obs.Size || p.outDim[n - 1] != Cfg.ActionCount)
                    throw new InvalidDataException($"policy expects {p.inDim[0]} obs / {p.outDim[n - 1]} actions, game has {Obs.Size} / {Cfg.ActionCount}");
                return p;
            }
        }

        static float[] ReadFloats(BinaryReader r, int count)
        {
            var bytes = r.ReadBytes(count * 4);
            var f = new float[count]; Buffer.BlockCopy(bytes, 0, f, 0, bytes.Length); return f;
        }

        /// <summary>Raw (unmasked) logits for an observation vector.</summary>
        public float[] Forward(float[] x)
        {
            float[] cur = x;
            for (int l = 0; l < w.Length; l++)
            {
                int ni = inDim[l], no = outDim[l];
                var next = new float[no];
                for (int o = 0; o < no; o++)
                {
                    float sum = b[l][o]; int row = o * ni;
                    for (int i = 0; i < ni; i++) { float v = cur[i]; if (v != 0f) sum += w[l][row + i] * v; }
                    next[o] = l < w.Length - 1 && sum < 0f ? 0f : sum;
                }
                cur = next;
            }
            return cur;
        }

        public int Act(GameSim sim, int team)
        {
            sim.FillObservation(team, obs);
            var logits = Forward(obs);
            if (Greedy)
            {
                int best = Cfg.NoopAction; float bv = float.NegativeInfinity;
                for (int a = 0; a < Cfg.ActionCount; a++)
                    if (logits[a] > bv && sim.IsValidAction(team, a)) { bv = logits[a]; best = a; }
                return best;
            }
            float max = float.NegativeInfinity;
            for (int a = 0; a < Cfg.ActionCount; a++) if (sim.IsValidAction(team, a) && logits[a] > max) max = logits[a];
            var pr = new double[Cfg.ActionCount]; double tot = 0;
            for (int a = 0; a < Cfg.ActionCount; a++) if (sim.IsValidAction(team, a)) { pr[a] = Math.Exp(logits[a] - max); tot += pr[a]; }
            double u = rng.NextDouble() * tot;
            for (int a = 0; a < Cfg.ActionCount; a++) { u -= pr[a]; if (pr[a] > 0 && u <= 0) return a; }
            return Cfg.NoopAction;
        }
    }
}
