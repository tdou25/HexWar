using System;

namespace HexWar
{
    /// <summary>
    /// Observation layout, always from the viewing team's perspective. Team 1 sees the map rotated 180°
    /// (tile i -> 99-i, dir d -> d+3) so a single shared policy can play either side.
    ///
    ///   [ board   10 x 10 x (3+n)  ]  slots 0-2: division ids in the tile (own=(i+1)/28, enemy=(15+i)/28, 0=empty)
    ///                                 attrs   : ownedByMe, isOutpost, isCapital, attackedByEnemy,
    ///                                           attackedByMe, myCount/3, enemyCount/3, frontline
    ///   [ own divisions   14 x K ]
    ///   [ enemy divisions 14 x K ]
    ///   [ globals 3              ]  hour/1440, VPs I hold/10, VPs enemy holds/10
    /// </summary>
    public static class Obs
    {
        public const int BoardAttrs = 8;                       // n
        public const int BoardChannels = Cfg.MaxStack + BoardAttrs;
        public const int BoardSize = Cfg.TileCount * BoardChannels;
        public const int K = 19;                               // per-division features
        public const int MatrixSize = Cfg.DivsPerTeam * K;
        public const int Globals = 3;
        public const int Size = BoardSize + 2 * MatrixSize + Globals;   // 1635
    }

    public sealed partial class GameSim
    {
        public static int ViewTile(int viewer, int tile) => viewer == 0 ? tile : Hex.Mirror(tile);

        /// <summary>Full observation vector (board | own matrix | enemy matrix | globals).</summary>
        public void FillObservation(int viewer, float[] buf)
        {
            Array.Clear(buf, 0, Obs.Size);
            FillBoard(viewer, buf, 0);
            FillDivisionMatrix(viewer, true, buf, Obs.BoardSize);
            FillDivisionMatrix(viewer, false, buf, Obs.BoardSize + Obs.MatrixSize);
            int g = Obs.BoardSize + 2 * Obs.MatrixSize;
            buf[g] = Hour / (float)Cfg.MaxHours;
            buf[g + 1] = VPsHeld(viewer) / 10f;
            buf[g + 2] = VPsHeld(1 - viewer) / 10f;
        }

        /// <summary>10x10x(3+n) tensor, row-major [row][col][channel], in the viewer's frame.</summary>
        public void FillBoard(int viewer, float[] buf, int off)
        {
            int C = Obs.BoardChannels;
            var slot = new int[Cfg.TileCount];
            var mine = new int[Cfg.TileCount];
            var theirs = new int[Cfg.TileCount];
            for (int pass = 0; pass < 2; pass++)
            {
                int team = pass == 0 ? viewer : 1 - viewer;
                for (int i = 0; i < Cfg.DivsPerTeam; i++)
                {
                    var d = Divs[team * Cfg.DivsPerTeam + i];
                    if (!d.Alive) continue;
                    int vt = ViewTile(viewer, d.Tile);
                    if (pass == 0) mine[vt]++; else theirs[vt]++;
                    if (slot[vt] < Cfg.MaxStack)
                        buf[off + vt * C + slot[vt]++] = (pass == 0 ? i + 1 : Cfg.DivsPerTeam + i + 1) / 28f;
                }
            }
            for (int vt = 0; vt < Cfg.TileCount; vt++)
            {
                int real = ViewTile(viewer, vt);
                var t = Tiles[real];
                int b = off + vt * C + Cfg.MaxStack;
                buf[b + 0] = t.Owner == viewer ? 1f : 0f;
                buf[b + 1] = t.VP == VPKind.Outpost ? 1f : 0f;
                buf[b + 2] = t.VP == VPKind.Capital ? 1f : 0f;
                buf[b + 3] = t.AttackedBy == 1 - viewer ? 1f : 0f;
                buf[b + 4] = t.AttackedBy == viewer ? 1f : 0f;
                buf[b + 5] = mine[vt] / 3f;
                buf[b + 6] = theirs[vt] / 3f;
                buf[b + 7] = IsFrontline(real) ? 1f : 0f;
            }
        }

        public bool IsFrontline(int tile)
        {
            for (int d = 0; d < 6; d++)
            {
                int n = Hex.Neighbor(tile, d);
                if (n >= 0 && Tiles[n].Owner != Tiles[tile].Owner) return true;
            }
            return false;
        }

        /// <summary>14 x K matrix for one side. Row = division index; dead divisions are all zeros.</summary>
        public void FillDivisionMatrix(int viewer, bool own, float[] buf, int off)
        {
            int team = own ? viewer : 1 - viewer;
            for (int i = 0; i < Cfg.DivsPerTeam; i++)
            {
                var d = Divs[team * Cfg.DivsPerTeam + i];
                int r = off + i * Obs.K;
                if (!d.Alive) continue;
                int vt = ViewTile(viewer, d.Tile);
                buf[r + 0] = 1f;
                buf[r + 1] = Hex.Q(vt) / 9f;
                buf[r + 2] = Hex.R(vt) / 9f;
                buf[r + 3] = d.Type == DivType.Infantry ? 1f : 0f;
                buf[r + 4] = d.Type == DivType.Tank ? 1f : 0f;
                buf[r + 5] = d.Supply / d.Stats.MaxSupply;
                buf[r + 6] = d.Org / d.Stats.MaxOrg;
                buf[r + 7] = d.Order >= 0 && !d.Attacking ? 1f : 0f;
                buf[r + 8] = d.Attacking ? 1f : 0f;
                buf[r + 9] = d.Defending ? 1f : 0f;
                buf[r + 10] = d.Progress;
                if (d.Order >= 0) buf[r + 11 + ViewDir(viewer, d.Order)] = 1f;   // one-hot order direction (view frame)
                buf[r + 17] = Tiles[d.Tile].VP != VPKind.None ? 1f : 0f;
                int inb = 0, foe = 0;
                for (int k = 0; k < 6; k++)
                {
                    int n = Hex.Neighbor(d.Tile, k);
                    if (n < 0) continue;
                    inb++; if (Tiles[n].Owner != d.Team) foe++;
                }
                buf[r + 18] = foe / (float)inb;   // 1.0 == fully surrounded by enemy territory
            }
        }
    }
}
