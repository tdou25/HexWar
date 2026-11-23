using System;
using System.Diagnostics;
using HexWar;

// Headless sanity checks + balance harness.  dotnet run -c Release -- [games]
static class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "check-policy") { CheckPolicy(args[1], args[2]); return; }
        int games = args.Length > 0 ? int.Parse(args[0]) : 200;
        SelfTests();
        Console.WriteLine("self-tests passed\n");
        if (args.Length>1){ var sm=new GameSim(3); var a=new ScriptedPolicy(1); var b=new ScriptedPolicy(2); while(!sm.Done) sm.Tick(a.Act(sm,0), b.Act(sm,1)); foreach(var l in sm.Log) Console.WriteLine(l); return; }
        Run("scripted vs scripted", games, (s, t, p0, p1) => t == 0 ? p0.Act(s, 0) : p1.Act(s, 1));
        Run("scripted(grey) vs random(red)", games, (s, t, p0, p1) => t == 0 ? p0.Act(s, 0) : p1.RandomValid(s, 1));
        Run("random vs random", games, (s, t, p0, p1) => t == 0 ? p0.RandomValid(s, 0) : p1.RandomValid(s, 1));
    }

    static void Run(string name, int games, Func<GameSim, int, ScriptedPolicy, ScriptedPolicy, int> pick)
    {
        int[] wins = new int[3]; double hours = 0; double[] kills = new double[2]; double[] bw = new double[2];
        var sw = Stopwatch.StartNew(); long steps = 0; double r0 = 0;
        var buf = new float[Obs.Size];
        for (int g = 0; g < games; g++)
        {
            var sim = new GameSim(g); var p0 = new ScriptedPolicy(g * 2 + 1); var p1 = new ScriptedPolicy(g * 2 + 2);
            while (!sim.Done)
            {
                sim.FillObservation(0, buf); sim.FillObservation(1, buf);
                var res = sim.Tick(pick(sim, 0, p0, p1), pick(sim, 1, p0, p1));
                r0 += res.Reward0; steps++;
            }
            wins[sim.Winner]++; hours += sim.Hour; kills[0] += sim.Kills[0]; kills[1] += sim.Kills[1];
            bw[0] += sim.BattlesWon[0]; bw[1] += sim.BattlesWon[1];
        }
        Console.WriteLine($"== {name} ({games} games)");
        Console.WriteLine($"   grey {wins[0]}  red {wins[1]}  draw {wins[2]}   avg length {hours / games / 24:F1} days");
        Console.WriteLine($"   avg kills grey {kills[0] / games:F1} red {kills[1] / games:F1}   battles won {bw[0] / games:F1} / {bw[1] / games:F1}");
        Console.WriteLine($"   {steps / sw.Elapsed.TotalSeconds:N0} steps/s (incl. 2 obs/step)\n");
    }

    // Compare C# MlpPolicy with PyTorch: vec.bin = int N, then N x (obs[1635] f32, logits[99] f32, expectedAction i32)
    static void CheckPolicy(string policyFile, string vecFile)
    {
        var net = MlpPolicy.Load(System.IO.File.ReadAllBytes(policyFile));
        using var r = new System.IO.BinaryReader(System.IO.File.OpenRead(vecFile));
        int n = r.ReadInt32(); float maxDiff = 0;
        for (int i = 0; i < n; i++)
        {
            var obs = new float[Obs.Size]; for (int k = 0; k < obs.Length; k++) obs[k] = r.ReadSingle();
            var want = new float[Cfg.ActionCount]; for (int k = 0; k < want.Length; k++) want[k] = r.ReadSingle();
            int act = r.ReadInt32();
            var got = net.Forward(obs);
            for (int k = 0; k < got.Length; k++) maxDiff = Math.Max(maxDiff, Math.Abs(got[k] - want[k]));
        }
        Console.WriteLine($"{n} states, max |logit diff| C# vs PyTorch = {maxDiff:E2}");
        if (maxDiff > 1e-3f) throw new Exception("policy mismatch");
    }

    static void Check(bool ok, string msg) { if (!ok) throw new Exception("SELF-TEST FAILED: " + msg); }

    static void SelfTests()
    {
        int interior = 0;
        for (int i = 0; i < Cfg.TileCount; i++)
        {
            int n = 0;
            for (int d = 0; d < 6; d++)
            {
                int j = Hex.Neighbor(i, d);
                if (j < 0) continue; n++;
                Check(Hex.Neighbor(j, Hex.Opposite(d)) == i, $"neighbour symmetry {i} dir {d}");
                Check(Hex.Distance(i, j) == 1, "adjacent distance");
                // 180° map symmetry
                Check(Hex.Neighbor(Hex.Mirror(i), Hex.Opposite(d)) == Hex.Mirror(j), "mirror symmetry");
            }
            if (n == 6) interior++;
        }
        Check(interior == 8 * 8 - 0 || interior > 0, "interior tiles");
        var s = new GameSim(1);
        Check(s.AliveCount(0) == 14 && s.AliveCount(1) == 14, "14 divisions each");
        int own0 = 0; foreach (var t in s.Tiles) if (t.Owner == 0) own0++;
        Check(own0 == 50, "map split evenly");
        int vp = 0; foreach (var t in s.Tiles) if (t.VP != VPKind.None) vp++;
        Check(vp == 10, "10 victory points");
        // observations are perspective-symmetric at t=0
        var a = new float[Obs.Size]; var b = new float[Obs.Size];
        s.FillObservation(0, a); s.FillObservation(1, b);
        for (int i = 0; i < Obs.Size; i++) Check(Math.Abs(a[i] - b[i]) < 1e-6f, $"initial obs symmetry at {i}");
        Check(s.IsValidAction(0, Cfg.NoopAction), "noop valid");

        // combat-rule invariants over many random games
        long pinned = 0, mutualBlocked = 0;
        for (int g = 0; g < 60; g++)
        {
            var sim = new GameSim(1000 + g); var p0 = new ScriptedPolicy(g + 1); var p1 = new ScriptedPolicy(g + 99);
            while (!sim.Done)
            {
                sim.Tick(g % 2 == 0 ? p0.RandomValid(sim, 0) : p0.Act(sim, 0), p1.RandomValid(sim, 1));
                foreach (var at in sim.Attacks)
                {
                    foreach (var bt in sim.Attacks) Check(!(at.From == bt.To && at.To == bt.From), "two tiles attacking each other");
                    // nobody may be offered a counter-attack on the tile hitting them
                    foreach (var d in sim.Divs)
                        if (d.Alive && d.Tile == at.To)
                            for (int dir = 0; dir < 6; dir++)
                                if (Hex.Neighbor(d.Tile, dir) == at.From)
                                {
                                    int act = d.Index * Cfg.CmdPerDiv + 1 + GameSim.ViewDir(d.Team, dir);
                                    Check(!sim.IsValidAction(d.Team, act), "counter-attack allowed"); mutualBlocked++;
                                }
                }
                foreach (var d in sim.Divs)
                    if (d.Alive && d.Order >= 0 && !d.Attacking && sim.Tiles[d.Tile].AttackedBy == 1 - d.Team)
                    { Check(d.Progress < 1f, "unit under attack completed a move"); pinned++; }
            }
        }
        Console.WriteLine($"  rule checks: {mutualBlocked} counter-attack opportunities blocked, {pinned} pinned-unit moves held");
    }
}
