using System.Collections.Generic;
using UnityEngine;

namespace HexWar.Play
{
    /// <summary>Draws the map, victory points, divisions, order arrows, edge battles and the frontline.</summary>
    public sealed class GameView : MonoBehaviour
    {
        public MatchManager Match;
        public int SelectedDiv = -1;                 // division id (team*14+index) or -1

        const float S = 1f;                          // hex circumradius
        static readonly float Sqrt3 = Mathf.Sqrt(3f);
        static readonly Color[] TileCol = { new Color(0.36f, 0.39f, 0.45f), new Color(0.50f, 0.22f, 0.22f) };
        static readonly Color[] UnitCol = { new Color(0.90f, 0.92f, 0.97f), new Color(1.00f, 0.42f, 0.36f) };

        static readonly Vector2[] Slot = { new Vector2(-0.42f, 0.20f), new Vector2(0.42f, 0.20f), new Vector2(0f, -0.30f) };

        sealed class DivView
        {
            public Transform root; public SpriteRenderer body, outline, sBg, sFill, oBg, oFill, arrow;
            public Vector3 pos; public bool init;
        }

        readonly SpriteRenderer[] tiles = new SpriteRenderer[Cfg.TileCount];
        readonly SpriteRenderer[] vpRing = new SpriteRenderer[Cfg.TileCount];
        readonly SpriteRenderer[] vpDot = new SpriteRenderer[Cfg.TileCount];
        readonly DivView[] divs = new DivView[28];
        readonly SpriteRenderer[] targetHi = new SpriteRenderer[6];
        SpriteRenderer selRing;
        readonly List<SpriteRenderer> front = new List<SpriteRenderer>();
        readonly List<SpriteRenderer> battleLine = new List<SpriteRenderer>();
        readonly List<SpriteRenderer> battleDot = new List<SpriteRenderer>();

        // ------------------------------------------------------------ layout
        public static Vector3 Center(int tile)
        {
            int q = Hex.Q(tile), r = Hex.R(tile);
            float x = 1.5f * S * q - 6.75f * S;
            float y = -(Sqrt3 * S * (r + 0.5f * (q & 1))) + Sqrt3 * S * 4.75f;
            return new Vector3(x, y, 0);
        }

        public int TileAt(Vector3 world)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < Cfg.TileCount; i++)
            {
                float d = ((Vector2)(Center(i) - world)).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return bd < S * S ? best : -1;
        }

        public Vector3 DivPos(int id) => divs[id].pos;

        // ------------------------------------------------------------ build
        SpriteRenderer SR(string name, Sprite sp, Transform parent, int order, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp; sr.sortingOrder = order; sr.color = c;
            return sr;
        }

        void Start()
        {
            SpriteKit.Build();
            for (int i = 0; i < Cfg.TileCount; i++)
            {
                tiles[i] = SR("Tile" + i, SpriteKit.Hex, transform, 0, Color.white);
                tiles[i].transform.position = Center(i);
                tiles[i].transform.localScale = Vector3.one * 0.95f;
                vpRing[i] = SR("VPRing", SpriteKit.Ring, tiles[i].transform, 3, Color.white);
                vpDot[i] = SR("VPDot", SpriteKit.Circle, tiles[i].transform, 3, Color.white);
                vpRing[i].enabled = vpDot[i].enabled = false;
            }
            for (int i = 0; i < 6; i++) { targetHi[i] = SR("Target" + i, SpriteKit.Hex, transform, 1, new Color(1, 1, 0.4f, 0.22f)); targetHi[i].enabled = false; }
            selRing = SR("Selection", SpriteKit.Ring, transform, 9, new Color(1, 0.95f, 0.3f)); selRing.enabled = false;

            for (int id = 0; id < 28; id++)
            {
                var dv = new DivView();
                var go = new GameObject("Div" + id);
                go.transform.SetParent(transform, false);
                dv.root = go.transform;
                bool tank = id % Cfg.DivsPerTeam >= Cfg.InfantryPerTeam;
                var shape = tank ? SpriteKit.Diamond : SpriteKit.Square;
                dv.outline = SR("outline", shape, dv.root, 10, new Color(0, 0, 0, 0.85f));
                dv.body = SR("body", shape, dv.root, 11, UnitCol[id / Cfg.DivsPerTeam]);
                dv.outline.transform.localScale = Vector3.one * (tank ? 0.74f : 0.62f);
                dv.body.transform.localScale = Vector3.one * (tank ? 0.62f : 0.50f);
                dv.sBg = SR("sBg", SpriteKit.Square, dv.root, 12, new Color(0, 0, 0, 0.8f));
                dv.sFill = SR("sFill", SpriteKit.Square, dv.root, 13, new Color(0.35f, 0.85f, 0.35f));
                dv.oBg = SR("oBg", SpriteKit.Square, dv.root, 12, new Color(0, 0, 0, 0.8f));
                dv.oFill = SR("oFill", SpriteKit.Square, dv.root, 13, new Color(0.35f, 0.75f, 1f));
                dv.arrow = SR("arrow", SpriteKit.Square, transform, 6, Color.white);
                divs[id] = dv;
            }
        }

        static void Line(SpriteRenderer sr, Vector3 a, Vector3 b, float width)
        {
            Vector3 d = b - a;
            sr.transform.position = (a + b) * 0.5f;
            sr.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            sr.transform.localScale = new Vector3(d.magnitude, width, 1);
        }

        SpriteRenderer Pooled(List<SpriteRenderer> pool, int i, Sprite sp, int order)
        {
            while (pool.Count <= i) pool.Add(SR("pooled", sp, transform, order, Color.white));
            pool[i].enabled = true;
            return pool[i];
        }
        static void HideFrom(List<SpriteRenderer> pool, int from) { for (int i = from; i < pool.Count; i++) pool[i].enabled = false; }

        // ------------------------------------------------------------ per-frame refresh
        void LateUpdate()
        {
            if (Match == null || Match.Sim == null || divs[0] == null) return;
            var sim = Match.Sim;

            for (int i = 0; i < Cfg.TileCount; i++)
            {
                var t = sim.Tiles[i];
                tiles[i].color = TileCol[t.Owner];
                if (t.VP == VPKind.None) continue;
                bool cap = t.VP == VPKind.Capital;
                Color c = UnitCol[t.Owner];
                vpRing[i].enabled = true; vpRing[i].color = c;
                vpRing[i].transform.localScale = Vector3.one * (cap ? 1.9f : 1.45f);
                vpDot[i].enabled = cap; vpDot[i].color = new Color(c.r, c.g, c.b, 0.9f);
                vpDot[i].transform.localScale = Vector3.one * 0.55f;
            }

            // frontline: every shared edge whose two tiles have different owners
            int fi = 0;
            for (int i = 0; i < Cfg.TileCount; i++)
                for (int d = 0; d < 3; d++)          // N, NE, SE covers each edge exactly once
                {
                    int n = Hex.Neighbor(i, d);
                    if (n < 0 || sim.Tiles[n].Owner == sim.Tiles[i].Owner) continue;
                    Vector3 a = Center(i), b = Center(n), mid = (a + b) * 0.5f;
                    Vector3 perp = Vector3.Cross(Vector3.forward, (b - a).normalized) * (S * 0.5f);
                    var sr = Pooled(front, fi++, SpriteKit.Square, 5);
                    sr.color = new Color(1f, 0.9f, 0.35f);
                    Line(sr, mid - perp, mid + perp, 0.11f);
                }
            HideFrom(front, fi);

            // edge battles
            for (int i = 0; i < sim.Attacks.Count; i++)
            {
                var atk = sim.Attacks[i];
                Vector3 a = Center(atk.From), b = Center(atk.To), mid = (a + b) * 0.5f;
                var ln = Pooled(battleLine, i, SpriteKit.Square, 6);
                ln.color = new Color(1f, 0.55f, 0.1f, 0.9f);
                Line(ln, a, b, 0.09f);
                var dot = Pooled(battleDot, i, SpriteKit.Circle, 14);
                float pulse = 0.38f + 0.10f * Mathf.Sin(Time.time * 9f);
                dot.transform.position = mid; dot.transform.localScale = Vector3.one * pulse;
                dot.color = new Color(1f, 0.8f, 0.2f, 0.95f);
            }
            HideFrom(battleLine, sim.Attacks.Count); HideFrom(battleDot, sim.Attacks.Count);

            // divisions
            var rank = new int[Cfg.TileCount];
            for (int id = 0; id < 28; id++)
            {
                var d = sim.Divs[id]; var dv = divs[id];
                dv.root.gameObject.SetActive(d.Alive);
                dv.arrow.enabled = false;
                if (!d.Alive) continue;

                int slot = Mathf.Min(rank[d.Tile]++, 2);
                Vector3 target = Center(d.Tile) + (Vector3)Slot[slot];
                if (d.Order >= 0)
                {
                    int to = Hex.Neighbor(d.Tile, d.Order);
                    if (to >= 0)
                    {
                        Vector3 tc = Center(to);
                        if (d.Attacking) target = Vector3.Lerp(target, (Center(d.Tile) + tc) * 0.5f, 0.6f);
                        else target = Vector3.Lerp(target, tc + (Vector3)Slot[0] * 0.3f, d.Progress);
                        dv.arrow.enabled = true;
                        var ac = UnitCol[d.Team]; ac.a = 0.55f; dv.arrow.color = ac;
                        Line(dv.arrow, dv.pos, tc, 0.05f);
                    }
                }
                if (!dv.init) { dv.pos = target; dv.init = true; }
                dv.pos = Vector3.Lerp(dv.pos, target, 1f - Mathf.Exp(-Time.deltaTime * 12f));
                dv.root.position = dv.pos;

                const float W = 0.52f;
                float sf = Mathf.Clamp01(d.Supply / d.Stats.MaxSupply), of = Mathf.Clamp01(d.Org / d.Stats.MaxOrg);
                Bar(dv.sBg, dv.sFill, -0.36f, W, sf);
                Bar(dv.oBg, dv.oFill, -0.45f, W, of);
                dv.body.color = d.Org <= 0 ? Color.Lerp(UnitCol[d.Team], Color.black, 0.55f) : UnitCol[d.Team];
            }

            // selection
            selRing.enabled = false;
            foreach (var h in targetHi) h.enabled = false;
            if (SelectedDiv >= 0 && sim.Divs[SelectedDiv].Alive)
            {
                var d = sim.Divs[SelectedDiv];
                selRing.enabled = true;
                selRing.transform.position = divs[SelectedDiv].pos;
                selRing.transform.localScale = Vector3.one * 0.95f;
                for (int k = 0; k < 6; k++)
                {
                    int n = Hex.Neighbor(d.Tile, k);
                    if (n < 0) continue;
                    targetHi[k].enabled = true;
                    targetHi[k].transform.position = Center(n);
                    targetHi[k].transform.localScale = Vector3.one * 0.9f;
                }
            }
            else SelectedDiv = -1;
        }

        static void Bar(SpriteRenderer bg, SpriteRenderer fill, float y, float w, float frac)
        {
            bg.transform.localPosition = new Vector3(0, y, 0); bg.transform.localScale = new Vector3(w + 0.04f, 0.085f, 1);
            fill.transform.localPosition = new Vector3(-w * 0.5f + w * frac * 0.5f, y, 0);
            fill.transform.localScale = new Vector3(Mathf.Max(0.0001f, w * frac), 0.06f, 1);
        }
    }
}
