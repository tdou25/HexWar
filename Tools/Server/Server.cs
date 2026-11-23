using System;
using System.IO;
using System.Threading.Tasks;
using HexWar;

// Batched environment server for Python training. Hosts N independent games and speaks a tiny binary
// protocol over stdin/stdout (stderr is for logs). Both teams of every game are exposed, each from its own
// perspective, so Python decides who is a learner, a past snapshot, or the built-in scripted bot (action -1).
//
//   server -> python (header, once):  int32 N, int32 obsSize, int32 actionCount
//   server -> python (after init / reset / every step), all little-endian:
//        float32 obs[N][2][obsSize]   uint8 mask[N][2][actionCount]   float32 reward[N][2] (raw, zero-sum)
//        uint8 done[N]   uint8 winner[N] (0 grey, 1 red, 2 draw, 255 n/a)   int32 episodeHours[N]
//     When done[i]=1 the game was auto-reset: obs/mask already belong to the NEW episode; reward is the final one.
//   python -> server:  int32 cmd  (0 = step: followed by int32 actions[N][2], -1 = scripted bot; 1 = reset all; 2 = quit)
static class Server
{
    sealed class Env
    {
        public GameSim Sim; public readonly ScriptedPolicy[] Pol = new ScriptedPolicy[2];
        public int Seed; readonly bool randomize; readonly Random meta;
        public Env(int seed, bool randomize) { Seed = seed; this.randomize = randomize; meta = new Random(seed * 7919 + 13); Reset(); }
        public void Reset()
        {
            Sim = new GameSim(Seed);
            for (int t = 0; t < 2; t++)
            {
                var p = new ScriptedPolicy(Seed * 2 + t);
                if (randomize) { p.IdleProb = (float)meta.NextDouble() * 0.5f; p.TargetSpread = 1 + meta.Next(4); p.RestFrac = 0.4f + (float)meta.NextDouble() * 0.5f; }
                Pol[t] = p;
            }
            Seed += 1000003;
        }
    }

    static int Main(string[] args)
    {
        int n = 32, seed = 1; bool randomize = true;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--envs") n = int.Parse(args[i + 1]);
            if (args[i] == "--seed") seed = int.Parse(args[i + 1]);
            if (args[i] == "--randomize") randomize = args[i + 1] != "0";
        }
        int D = Obs.Size, A = Cfg.ActionCount;
        var inp = new BufferedStream(Console.OpenStandardInput(), 1 << 20);
        var outp = new BufferedStream(Console.OpenStandardOutput(), 1 << 20);

        var envs = new Env[n];
        for (int i = 0; i < n; i++) envs[i] = new Env(seed * 100 + i, randomize);

        var obs = new float[n * 2 * D]; var mask = new byte[n * 2 * A]; var rew = new float[n * 2];
        var done = new byte[n]; var winner = new byte[n]; var hours = new int[n];
        var obsBytes = new byte[obs.Length * 4]; var rewBytes = new byte[rew.Length * 4]; var hourBytes = new byte[n * 4];
        var actBuf = new byte[n * 2 * 4]; var acts = new int[n * 2];

        WriteInt(outp, n); WriteInt(outp, D); WriteInt(outp, A);
        FillAll(envs, obs, mask, D, A); Flush(outp, obs, mask, rew, done, winner, hours, obsBytes, rewBytes, hourBytes);

        while (true)
        {
            int cmd = ReadInt(inp);
            if (cmd == 2) break;
            Array.Clear(rew, 0, rew.Length); Array.Clear(done, 0, n); Array.Clear(hours, 0, n);
            for (int i = 0; i < n; i++) winner[i] = 255;
            if (cmd == 1) { Parallel.For(0, n, i => envs[i].Reset()); }
            else
            {
                ReadExact(inp, actBuf, actBuf.Length);
                Buffer.BlockCopy(actBuf, 0, acts, 0, actBuf.Length);
                Parallel.For(0, n, i =>
                {
                    var e = envs[i]; var s = e.Sim;
                    int a0 = acts[2 * i] >= 0 ? acts[2 * i] : e.Pol[0].Act(s, 0);
                    int a1 = acts[2 * i + 1] >= 0 ? acts[2 * i + 1] : e.Pol[1].Act(s, 1);
                    var r = s.Tick(a0, a1);
                    rew[2 * i] = r.Reward0; rew[2 * i + 1] = -r.Reward0;
                    if (r.Done) { done[i] = 1; winner[i] = (byte)r.Winner; hours[i] = s.Hour; e.Reset(); }
                });
            }
            FillAll(envs, obs, mask, D, A);
            Flush(outp, obs, mask, rew, done, winner, hours, obsBytes, rewBytes, hourBytes);
        }
        return 0;
    }

    static void FillAll(Env[] envs, float[] obs, byte[] mask, int D, int A)
    {
        Parallel.For(0, envs.Length, i =>
        {
            var s = envs[i].Sim;
            for (int t = 0; t < 2; t++)
            {
                int slot = 2 * i + t;
                var o = new float[D]; s.FillObservation(t, o);
                Buffer.BlockCopy(o, 0, obs, slot * D * 4, D * 4);
                for (int a = 0; a < A; a++) mask[slot * A + a] = (byte)(s.IsValidAction(t, a) ? 1 : 0);
            }
        });
    }

    static void Flush(Stream o, float[] obs, byte[] mask, float[] rew, byte[] done, byte[] winner, int[] hours,
                      byte[] obsB, byte[] rewB, byte[] hourB)
    {
        Buffer.BlockCopy(obs, 0, obsB, 0, obsB.Length); o.Write(obsB, 0, obsB.Length);
        o.Write(mask, 0, mask.Length);
        Buffer.BlockCopy(rew, 0, rewB, 0, rewB.Length); o.Write(rewB, 0, rewB.Length);
        o.Write(done, 0, done.Length); o.Write(winner, 0, winner.Length);
        Buffer.BlockCopy(hours, 0, hourB, 0, hourB.Length); o.Write(hourB, 0, hourB.Length);
        o.Flush();
    }

    static void WriteInt(Stream s, int v) { s.Write(BitConverter.GetBytes(v), 0, 4); }
    static int ReadInt(Stream s) { var b = new byte[4]; ReadExact(s, b, 4); return BitConverter.ToInt32(b, 0); }
    static void ReadExact(Stream s, byte[] b, int n)
    {
        int got = 0;
        while (got < n) { int r = s.Read(b, got, n - got); if (r <= 0) Environment.Exit(0); got += r; }
    }
}
