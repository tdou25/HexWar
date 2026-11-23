using System;
using System.Collections.Generic;

namespace HexWar
{
    /// <summary>
    /// Simple baseline opponent: every hour pick a rested division and nudge it toward the nearest
    /// enemy victory points. Useful as a sparring partner, for smoke tests and as a curriculum start.
    /// </summary>
    public sealed class ScriptedPolicy
    {
        readonly Random rng;
        public float IdleProb = 0.15f;
        public int TargetSpread = 2;       // how many of the nearest enemy VPs the army splits across
        public float RestFrac = 0.7f;      // a division must have this much org before it is sent forward
        public ScriptedPolicy(int seed = 1) { rng = new Random(seed); }

        public int RandomValid(GameSim s, int team)
        {
            for (int tries = 0; tries < 50; tries++)
            {
                int a = rng.Next(Cfg.ActionCount);
                if (s.IsValidAction(team, a)) return a;
            }
            return Cfg.NoopAction;
        }

        public int Act(GameSim s, int team)
        {
            if (rng.NextDouble() < IdleProb) return Cfg.NoopAction;

            var cands = new List<Division>();
            for (int i = 0; i < Cfg.DivsPerTeam; i++)
            {
                var d = s.Divs[team * Cfg.DivsPerTeam + i];
                if (d.Alive && d.Order < 0 && d.Org >= RestFrac * d.Stats.MaxOrg && d.Supply >= 0.25f * d.Stats.MaxSupply)
                    cands.Add(d);
            }
            if (cands.Count == 0) return Cfg.NoopAction;
            var div = cands[rng.Next(cands.Count)];

            var targets = new List<int>();
            for (int t = 0; t < Cfg.TileCount; t++)
                if (s.Tiles[t].VP != VPKind.None && s.Tiles[t].Home != team && s.Tiles[t].Owner != team) targets.Add(t);
            if (targets.Count == 0) return Cfg.NoopAction;
            targets.Sort((a, b) => Hex.Distance(div.Tile, a).CompareTo(Hex.Distance(div.Tile, b)));
            int target = targets[div.Index % Math.Min(TargetSpread, targets.Count)];

            int cur = Hex.Distance(div.Tile, target), bestDir = -1, bestD = cur, ties = 0;
            for (int dir = 0; dir < 6; dir++)
            {
                int n = Hex.Neighbor(div.Tile, dir);
                if (n < 0) continue;
                int dist = Hex.Distance(n, target);
                if (dist < bestD) { bestD = dist; bestDir = dir; ties = 1; }
                else if (dist == bestD && bestDir >= 0 && rng.Next(++ties) == 0) bestDir = dir;
            }
            if (bestDir < 0) return Cfg.NoopAction;
            int a2 = (div.Index) * Cfg.CmdPerDiv + 1 + GameSim.ViewDir(team, bestDir);
            return s.IsValidAction(team, a2) ? a2 : Cfg.NoopAction;
        }
    }
}
