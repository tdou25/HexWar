using System;

namespace HexWar
{
    /// <summary>
    /// Flat-top hexes, odd columns shoved down ("odd-q"). Every interior tile has 6 neighbours:
    /// N, NE, SE, S, SW, NW (dir 0..5). Tile index = r * 10 + q.
    /// The 10x10 map is symmetric under 180° rotation: tile i  <->  99 - i,  dir d <-> (d+3)%6.
    /// </summary>
    public static class Hex
    {
        static readonly int[] EvenDQ = { 0, 1, 1, 0, -1, -1 };
        static readonly int[] EvenDR = { -1, -1, 0, 1, 0, -1 };
        static readonly int[] OddDQ  = { 0, 1, 1, 0, -1, -1 };
        static readonly int[] OddDR  = { -1, 0, 1, 1, 1, 0 };

        static readonly int[,] nbr = BuildNeighbours();

        static int[,] BuildNeighbours()
        {
            var t = new int[Cfg.TileCount, Cfg.DirCount];
            for (int i = 0; i < Cfg.TileCount; i++)
            {
                int q = i % Cfg.Width, r = i / Cfg.Width;
                for (int d = 0; d < 6; d++)
                {
                    int nq = q + ((q & 1) == 0 ? EvenDQ[d] : OddDQ[d]);
                    int nr = r + ((q & 1) == 0 ? EvenDR[d] : OddDR[d]);
                    t[i, d] = (nq < 0 || nq >= Cfg.Width || nr < 0 || nr >= Cfg.Height) ? -1 : nr * Cfg.Width + nq;
                }
            }
            return t;
        }

        public static int Index(int q, int r) => r * Cfg.Width + q;
        public static int Q(int tile) => tile % Cfg.Width;
        public static int R(int tile) => tile / Cfg.Width;
        /// <summary>Neighbour tile index in direction d, or -1 if off-map.</summary>
        public static int Neighbor(int tile, int d) => nbr[tile, d];
        public static int Opposite(int d) => (d + 3) % 6;
        public static int Mirror(int tile) => Cfg.TileCount - 1 - tile;

        public static int Distance(int a, int b)
        {
            Cube(a, out int ax, out int ay, out int az);
            Cube(b, out int bx, out int by, out int bz);
            return Math.Max(Math.Abs(ax - bx), Math.Max(Math.Abs(ay - by), Math.Abs(az - bz)));
        }

        static void Cube(int tile, out int x, out int y, out int z)
        {
            int q = Q(tile), r = R(tile);
            x = q; z = r - (q - (q & 1)) / 2; y = -x - z;
        }

        /// <summary>Direction from tile a to adjacent tile b, or -1.</summary>
        public static int DirTo(int a, int b)
        {
            for (int d = 0; d < 6; d++) if (nbr[a, d] == b) return d;
            return -1;
        }
    }
}
