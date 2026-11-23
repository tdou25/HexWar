using System;
using System.Collections.Generic;

namespace HexWar
{
    /// <summary>
    /// Deterministic hour-by-hour simulation. Each call to Tick() advances one in-game hour and
    /// accepts at most one division order per team.
    /// </summary>
    public sealed partial class GameSim
    {
        public readonly Tile[] Tiles = new Tile[Cfg.TileCount];
        public readonly Division[] Divs = new Division[2 * Cfg.DivsPerTeam];
        public readonly List<Attack> Attacks = new List<Attack>();   // battles fought during the last tick
        public readonly List<string> Log = new List<string>();
        public int Hour;
        public bool Done;
        public int Winner = -1;
        public readonly int[] Kills = new int[2];        // enemy divisions destroyed by team
        public readonly int[] BattlesWon = new int[2];

        Random rng;
        float reward;
        readonly List<int[]> prevAttacks = new List<int[]>();
        readonly float[] dOrg = new float[28], dSup = new float[28];

        public static readonly int[] CapitalTile = { Hex.Index(0, 4), Hex.Index(9, 5) };
        // grey outposts; red's are the 180° mirror
        static readonly int[] GreyOutposts = { Hex.Index(4, 2), Hex.Index(4, 7), Hex.Index(2, 1), Hex.Index(2, 8) };

        public GameSim(int seed = 0) { Reset(seed); }

        public void Reset(int seed)
        {
            rng = new Random(seed);
            Hour = 0; Done = false; Winner = -1; Attacks.Clear(); Log.Clear();
            Kills[0] = Kills[1] = 0; BattlesWon[0] = BattlesWon[1] = 0;

            for (int i = 0; i < Cfg.TileCount; i++)
                Tiles[i] = new Tile { Owner = Hex.Q(i) <= 4 ? 0 : 1, VP = VPKind.None, Home = -1 };
            for (int t = 0; t < 2; t++)
            {
                Mark(t == 0 ? CapitalTile[0] : CapitalTile[1], VPKind.Capital, t);
                foreach (int o in GreyOutposts) Mark(t == 0 ? o : Hex.Mirror(o), VPKind.Outpost, t);
            }

            // Infantry hold the whole front line (col 4 / col 5), tanks wait one column behind.
            int[] tankRows = { 2, 4, 5, 7 };
            for (int team = 0; team < 2; team++)
                for (int i = 0; i < Cfg.DivsPerTeam; i++)
                {
                    bool tank = i >= Cfg.InfantryPerTeam;
                    int tile = tank ? Hex.Index(3, tankRows[i - Cfg.InfantryPerTeam]) : Hex.Index(4, i);
                    if (team == 1) tile = Hex.Mirror(tile);
                    var st = Cfg.Stats[tank ? 1 : 0];
                    Divs[team * Cfg.DivsPerTeam + i] = new Division
                    {
                        Id = team * Cfg.DivsPerTeam + i, Team = team, Index = i,
                        Type = tank ? DivType.Tank : DivType.Infantry, Stats = st, Alive = true,
                        Tile = tile, Supply = st.MaxSupply, Org = st.MaxOrg
                    };
                }
        }

        void Mark(int tile, VPKind k, int home) { Tiles[tile].VP = k; Tiles[tile].Home = home; }

        // ------------------------------------------------------------------ queries
        public int CountAt(int tile, int team)
        {
            int c = 0, b = team * Cfg.DivsPerTeam;
            for (int i = b; i < b + Cfg.DivsPerTeam; i++) if (Divs[i].Alive && Divs[i].Tile == tile) c++;
            return c;
        }
        public int AliveCount(int team)
        {
            int c = 0, b = team * Cfg.DivsPerTeam;
            for (int i = b; i < b + Cfg.DivsPerTeam; i++) if (Divs[i].Alive) c++;
            return c;
        }
        /// <summary>Enemy-home VP tiles that `team` currently owns (outposts, capital).</summary>
        public void CapturedVPs(int team, out int outposts, out bool capital)
        {
            outposts = 0; capital = false;
            for (int i = 0; i < Cfg.TileCount; i++)
            {
                var t = Tiles[i];
                if (t.VP == VPKind.None || t.Home == team || t.Owner != team) continue;
                if (t.VP == VPKind.Outpost) outposts++; else capital = true;
            }
        }
        public int VPsHeld(int team)
        {
            int c = 0;
            foreach (var t in Tiles) if (t.VP != VPKind.None && t.Owner == team) c++;
            return c;
        }

        // ------------------------------------------------------------------ actions
        // action = div * 7 + cmd  (cmd 0 = cancel order, cmd 1..6 = direction in the team's *own view*)
        // action 98 = no-op. For team 1 the view is rotated 180°, so view dir d == real dir (d+3)%6.
        public static int ViewDir(int team, int d) => team == 0 ? d : (d + 3) % 6;

        public bool IsValidAction(int team, int a)
        {
            if (a == Cfg.NoopAction) return true;
            if (a < 0 || a >= Cfg.NoopAction) return false;
            var d = Divs[team * Cfg.DivsPerTeam + a / Cfg.CmdPerDiv];
            int cmd = a % Cfg.CmdPerDiv;
            if (!d.Alive) return false;
            if (cmd == 0) return d.Order >= 0;
            if (d.Org < d.Stats.MaxOrg * Cfg.MinOrgFracToOrder) return false;
            int dir = ViewDir(team, cmd - 1);
            int to = Hex.Neighbor(d.Tile, dir);
            if (d.Order == dir || to < 0) return false;
            return !IsAttackedFrom(d.Tile, to);   // you can't counter-attack the tile that is attacking you
        }

        /// <summary>True if, in the last resolved hour, units on `from` were attacking tile `tile`.</summary>
        public bool IsAttackedFrom(int tile, int from)
        {
            foreach (var a in Attacks) if (a.To == tile && a.From == from) return true;
            return false;
        }

        void ApplyAction(int team, int a)
        {
            if (a == Cfg.NoopAction || !IsValidAction(team, a)) return;
            var d = Divs[team * Cfg.DivsPerTeam + a / Cfg.CmdPerDiv];
            int cmd = a % Cfg.CmdPerDiv;
            d.Progress = 0;
            d.Order = cmd == 0 ? -1 : ViewDir(team, cmd - 1);
        }

        // ------------------------------------------------------------------ the hour
        public StepResult Tick(int action0, int action1)
        {
            var res = new StepResult { Winner = Winner, Done = Done };
            if (Done) return res;

            reward = 0;
            ApplyAction(0, action0);      // validated against last hour's battles (Attacks not yet cleared)
            ApplyAction(1, action1);

            prevAttacks.Clear();
            foreach (var a in Attacks) prevAttacks.Add(new[] { a.From, a.To });
            Attacks.Clear();
            foreach (var t in Tiles) t.AttackedBy = -1;
            foreach (var d in Divs) { d.Attacking = d.Defending = d.InCombat = false; }

            FindAttacks();
            ResolveCombat();
            Movement();
            Encirclement();
            Regen();
            CheckVictory();

            Hour++;
            if (!Done && Hour >= Cfg.MaxHours) { Done = true; Winner = 2; Say("Day 60 reached: draw."); }

            res.Reward0 = reward; res.Done = Done; res.Winner = Winner;
            return res;
        }

        void Credit(int team, float amount) { reward += team == 0 ? amount : -amount; }
        void Say(string s) { Log.Add($"D{Hour / 24 + 1} {Hour % 24:00}h  {s}"); if (Log.Count > 10) Log.RemoveAt(0); }
        static string TeamName(int t) => t == 0 ? "Grey" : "Red";

        // 1. orders into enemy-occupied tiles become edge battles
        void FindAttacks()
        {
            foreach (var d in Divs)
            {
                if (!d.Alive || d.Order < 0) continue;
                if (d.Org <= 0) { d.Order = -1; d.Progress = 0; continue; }
                int to = Hex.Neighbor(d.Tile, d.Order);
                if (to < 0) { d.Order = -1; continue; }
                if (CountAt(to, 1 - d.Team) == 0) continue;

                d.Attacking = true; d.Progress = 0;
                Attack atk = null;
                foreach (var a in Attacks) if (a.From == d.Tile && a.To == to) { atk = a; break; }
                if (atk == null) { atk = new Attack { From = d.Tile, To = to, Team = d.Team }; Attacks.Add(atk); }
                atk.Attackers.Add(d);
            }

            // Two tiles can't attack each other: the side that was already being attacked last hour keeps
            // defending; if both started this very hour, a coin flip decides who actually attacks.
            bool again = true;
            while (again)
            {
                again = false;
                foreach (var a in Attacks)
                {
                    Attack rev = null;
                    foreach (var b in Attacks) if (b.From == a.To && b.To == a.From) { rev = b; break; }
                    if (rev == null) continue;
                    bool aWasAttacked = false, revWasAttacked = false;
                    foreach (var p in prevAttacks)
                    {
                        if (p[0] == a.To && p[1] == a.From) aWasAttacked = true;      // rev's tile was hitting a's tile
                        if (p[0] == a.From && p[1] == a.To) revWasAttacked = true;
                    }
                    Attack victim = aWasAttacked && !revWasAttacked ? a
                                  : revWasAttacked && !aWasAttacked ? rev
                                  : (rng.Next(2) == 0 ? a : rev);
                    foreach (var d in victim.Attackers) { d.Attacking = false; d.Order = -1; d.Progress = 0; }
                    Attacks.Remove(victim);
                    again = true;
                    break;
                }
            }
            foreach (var a in Attacks) Tiles[a.To].AttackedBy = a.Team;
        }

        // 2. simultaneous damage on every battle edge
        void ResolveCombat()
        {
            if (Attacks.Count == 0) return;
            Array.Clear(dOrg, 0, dOrg.Length); Array.Clear(dSup, 0, dSup.Length);
            var perTile = new int[Cfg.TileCount];
            foreach (var a in Attacks) perTile[a.To]++;

            var defenders = new List<Division>(3);
            foreach (var atk in Attacks)
            {
                int defTeam = 1 - atk.Team;
                defenders.Clear();
                for (int i = defTeam * 14; i < defTeam * 14 + 14; i++)
                    if (Divs[i].Alive && Divs[i].Tile == atk.To) defenders.Add(Divs[i]);
                bool vp = Tiles[atk.To].VP != VPKind.None && Tiles[atk.To].Owner == defTeam;

                foreach (var a in atk.Attackers)
                {
                    a.InCombat = true;
                    Fire(a, defenders[rng.Next(defenders.Count)], 1f, false, vp);
                }
                foreach (var dd in defenders)
                {
                    dd.InCombat = true; dd.Defending = true;
                    // defenders that are busy attacking elsewhere don't shoot back; others split fire across edges
                    if (!dd.Attacking && dd.Org > 0)
                        Fire(dd, atk.Attackers[rng.Next(atk.Attackers.Count)], 1f / perTile[atk.To], true, vp);
                }
            }

            foreach (var d in Divs)
            {
                if (!d.Alive || !d.InCombat) continue;
                d.Org -= dOrg[d.Id];
                d.Supply -= dSup[d.Id] + (d.Attacking ? Cfg.AttackSupplyDrain : Cfg.DefendSupplyDrain);
            }

            // supply exhaustion kills
            foreach (var d in Divs) if (d.Alive && d.Supply <= 0) Kill(d, "ran out of supply");

            // broken units: attackers disengage, defenders fall back (or stay stuck at 0 org)
            foreach (var d in Divs)
            {
                if (!d.Alive || d.Org > 0) continue;
                d.Org = 0;
                if (d.Attacking) { d.Order = -1; d.Progress = 0; d.Attacking = false; }
                else if (d.Defending) TryRetreat(d);
            }

            // who won each edge battle?
            foreach (var atk in Attacks)
            {
                bool attAlive = false;
                foreach (var a in atk.Attackers) if (a.Alive && a.Attacking) { attAlive = true; break; }
                bool defAlive = CountAt(atk.To, 1 - atk.Team) > 0;
                int winner = -1;
                if (attAlive && !defAlive) winner = atk.Team;
                else if (!attAlive && defAlive) winner = 1 - atk.Team;
                if (winner >= 0) { Credit(winner, Cfg.RBattleWin); BattlesWon[winner]++; }
            }
        }

        void Fire(Division s, Division t, float scale, bool shooterIsDefender, bool vpDefence)
        {
            float h = t.Stats.Hardness;
            float eff = s.Stats.SoftAttack * (1 - h) + s.Stats.HardAttack * h;
            float def = t.Stats.Defense * (!shooterIsDefender && vpDefence ? Cfg.VPDefenseMul : 1f);
            float pierce = s.Stats.Breakthrough / (s.Stats.Breakthrough + def);
            float factor = Cfg.PierceFloor + (1 - Cfg.PierceFloor) * pierce;
            float supply = Cfg.SupplyOutputFloor + (1 - Cfg.SupplyOutputFloor) * (s.Supply / s.Stats.MaxSupply);
            float atk = shooterIsDefender && vpDefence ? Cfg.VPAttackMul : 1f;
            float roll = Cfg.DamageRollLo + (float)rng.NextDouble() * (Cfg.DamageRollHi - Cfg.DamageRollLo);
            float o = eff * factor * supply * atk * scale * roll;
            dOrg[t.Id] += o * Cfg.OrgDamageK;
            dSup[t.Id] += o * Cfg.SupplyDamageK * (t.Attacking ? Cfg.AttackerSupplyMul : 1f);
        }

        void TryRetreat(Division d)
        {
            int from = -1;
            foreach (var a in Attacks) if (a.To == d.Tile) { from = a.From; break; }
            int best = -1, bestScore = -1;
            for (int dir = 0; dir < 6; dir++)
            {
                int n = Hex.Neighbor(d.Tile, dir);
                if (n < 0 || Tiles[n].Owner != d.Team) continue;
                if (CountAt(n, 1 - d.Team) > 0 || CountAt(n, d.Team) >= Cfg.MaxStack) continue;
                int score = from >= 0 ? Hex.Distance(n, from) * 4 + rng.Next(4) : rng.Next(4);
                if (score > bestScore) { bestScore = score; best = n; }
            }
            if (best < 0) return;   // nowhere to run; stays broken (and gets ground down)
            d.Tile = best; d.Order = -1; d.Progress = 0; d.Defending = false;
        }

        void Kill(Division d, string why)
        {
            d.Alive = false; d.Order = -1; d.Attacking = d.Defending = false;
            int killer = 1 - d.Team;
            Kills[killer]++;
            Credit(killer, Cfg.RKill);
            Say($"{TeamName(d.Team)} {d.Stats.Name} #{d.Index} destroyed ({why}).");
        }

        // 3. advance non-fighting movers; arrival captures the tile
        void Movement()
        {
            foreach (var d in Divs)
            {
                if (!d.Alive || d.Order < 0 || d.Attacking) continue;
                if (d.Org <= 0) { d.Order = -1; d.Progress = 0; continue; }
                int to = Hex.Neighbor(d.Tile, d.Order);
                if (to < 0) { d.Order = -1; continue; }
                bool underAttack = Tiles[d.Tile].AttackedBy == 1 - d.Team;
                d.Progress = Math.Min(underAttack ? 0.95f : 1f, d.Progress + d.Stats.Speed);   // pinned: can't fully leave
                if (d.Progress < 1f) continue;
                if (CountAt(to, 1 - d.Team) > 0) continue;               // enemy showed up: next hour this is an attack
                if (CountAt(to, d.Team) >= Cfg.MaxStack) continue;       // tile full: wait at the edge

                d.Tile = to; d.Order = -1; d.Progress = 0;
                var tile = Tiles[to];
                if (tile.Owner == d.Team) continue;
                tile.Owner = d.Team;
                if (tile.VP == VPKind.Capital) { Credit(d.Team, Cfg.RCapital); Say($"{TeamName(d.Team)} captured the {TeamName(tile.Home)} command center!"); }
                else if (tile.VP == VPKind.Outpost) { Credit(d.Team, Cfg.ROutpost); Say($"{TeamName(d.Team)} captured a {TeamName(tile.Home)} outpost."); }
            }
        }

        // 4. a broken division whose every neighbouring tile is enemy territory is wiped out
        void Encirclement()
        {
            foreach (var d in Divs)
            {
                if (!d.Alive || d.Org > 0) continue;
                bool surrounded = true;
                for (int dir = 0; dir < 6 && surrounded; dir++)
                {
                    int n = Hex.Neighbor(d.Tile, dir);
                    if (n >= 0 && Tiles[n].Owner == d.Team) surrounded = false;
                }
                if (surrounded) Kill(d, "encircled");
            }
        }

        // 5. organisation slowly recovers out of combat
        void Regen()
        {
            foreach (var d in Divs)
            {
                if (!d.Alive || d.InCombat || d.Org >= d.Stats.MaxOrg) continue;
                d.Org = Math.Min(d.Stats.MaxOrg, d.Org + d.Stats.OrgRegen * (d.Order >= 0 ? Cfg.MoveOrgRegenMul : 1f));
            }
        }

        void CheckVictory()
        {
            bool[] won = new bool[2];
            for (int t = 0; t < 2; t++)
            {
                CapturedVPs(t, out int op, out bool cap);
                won[t] = op == 4 || (op >= 2 && cap) || AliveCount(1 - t) == 0;
            }
            if (!won[0] && !won[1]) return;
            Done = true;
            Winner = won[0] && won[1] ? 2 : (won[0] ? 0 : 1);
            if (Winner < 2) { Credit(Winner, Cfg.RVictory); Say($"{TeamName(Winner)} wins!"); }
        }
    }
}
