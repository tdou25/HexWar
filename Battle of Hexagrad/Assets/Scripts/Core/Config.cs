// All tunable game constants live here. Core has NO UnityEngine dependency so it can be
// run headless (see Tools/Headless) for balance testing and fast RL rollouts.
namespace HexWar
{
    public enum DivType { Infantry = 0, Tank = 1 }
    public enum VPKind { None = 0, Outpost = 1, Capital = 2 }

    public sealed class UnitStats
    {
        public string Name;
        public float SoftAttack, HardAttack;   // damage vs soft / hard targets (blended by target hardness)
        public float Breakthrough;             // offensive "pierce" against enemy defense
        public float Defense;                  // reduces incoming damage (vs attacker breakthrough)
        public float Hardness;                 // 0 = fully soft, 1 = fully hard
        public float Speed;                    // fraction of a tile crossed per hour
        public float MaxOrg;                   // combat cohesion
        public float MaxSupply;                // "true health"; never replenished
        public float OrgRegen;                 // org per hour when out of combat
    }

    public static class Cfg
    {
        // ---- board ----
        public const int Width = 10, Height = 10, TileCount = 100;
        public const int DirCount = 6;               // 0=N 1=NE 2=SE 3=S 4=SW 5=NW
        public const int MaxStack = 3;               // divisions per tile

        // ---- forces ----
        public const int InfantryPerTeam = 10, TanksPerTeam = 4;
        public const int DivsPerTeam = 14;           // rows 0-9 infantry, 10-13 tanks

        // ---- time ----
        public const int HoursPerDay = 24, MaxDays = 60;
        public const int MaxHours = HoursPerDay * MaxDays;   // 1440 steps, 1 step = 1 hour

        // ---- action space: one division order per team per hour ----
        public const int CmdPerDiv = 7;              // 0 = cancel order, 1..6 = move/attack in direction
        public const int NoopAction = DivsPerTeam * CmdPerDiv;   // 98
        public const int ActionCount = NoopAction + 1;           // 99

        // ---- unit stats ----
        public static readonly UnitStats[] Stats =
        {
            new UnitStats { Name="Infantry", SoftAttack=6f, HardAttack=1.5f, Breakthrough=6f,  Defense=14f,
                            Hardness=0.10f, Speed=1f/36f, MaxOrg=60f, MaxSupply=100f, OrgRegen=0.6f },
            new UnitStats { Name="Tank",     SoftAttack=7f, HardAttack=11f,  Breakthrough=16f, Defense=8f,
                            Hardness=0.60f, Speed=1f/18f,  MaxOrg=40f, MaxSupply=80f,  OrgRegen=0.5f },
        };

        // ---- combat model (per hour, per firing division) ----
        public const float PierceFloor      = 0.25f;  // damage factor = floor + (1-floor) * BT/(BT+DEF)
        public const float OrgDamageK       = 0.08f;  // org lost per point of effective damage
        public const float SupplyDamageK    = 0.10f;  // supply lost per point of effective damage
        public const float AttackerSupplyMul= 1.25f;  // attackers bleed supply faster
        public const float AttackSupplyDrain= 0.14f;  // flat ammo/fuel burn per hour while attacking
        public const float DefendSupplyDrain= 0.05f;  // ...while defending
        public const float SupplyOutputFloor= 0.35f;  // output = floor + (1-floor)*supply/maxSupply
        public const float DamageRollLo = 0.75f, DamageRollHi = 1.25f;
        public const float MoveOrgRegenMul  = 0.5f;   // org regen multiplier while carrying a move order
        public const float MinOrgFracToOrder= 0.10f;  // broken units can't be ordered forward
        // victory-point defender buffs ("slight")
        public const float VPDefenseMul = 1.25f, VPAttackMul = 1.10f;

        // ---- rewards (zero-sum; loser gets the negative) ----
        public const float RBattleWin = 1f, RKill = 25f, ROutpost = 100f, RCapital = 200f, RVictory = 1000f;
    }
}
