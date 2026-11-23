using System.Collections.Generic;

namespace HexWar
{
    public sealed class Tile
    {
        public int Owner;            // 0 = grey, 1 = red
        public VPKind VP;
        public int Home;             // original owner of a VP tile (-1 for plain tiles)
        public int AttackedBy = -1;  // team whose units are attacking this tile this hour
    }

    public sealed class Division
    {
        public int Id;               // team * 14 + index
        public int Team;             // 0 grey, 1 red
        public int Index;            // 0..13 (0-9 infantry, 10-13 tanks)
        public DivType Type;
        public UnitStats Stats;
        public bool Alive;
        public int Tile;             // the tile the division occupies (while moving it still "belongs" to its origin)
        public float Supply, Org;
        public int Order = -1;       // direction of current move/attack order, -1 = none
        public float Progress;       // 0..1 along the edge toward the target tile
        public bool Attacking, Defending, InCombat;   // transient per-hour flags
    }

    /// <summary>One edge battle this hour: attackers on From hitting the defenders on To.</summary>
    public sealed class Attack
    {
        public int From, To, Team;
        public readonly List<Division> Attackers = new List<Division>();
    }

    public struct StepResult
    {
        public float Reward0;   // zero-sum reward from team 0's point of view (team 1 gets the negative)
        public bool Done;
        public int Winner;      // -1 ongoing, 0 grey, 1 red, 2 draw (timeout)
    }
}
