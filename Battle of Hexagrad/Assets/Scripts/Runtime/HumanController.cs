using UnityEngine;

namespace HexWar.Play
{
    /// <summary>
    /// Mouse/keyboard control + minimalist HUD (all IMGUI, so it works with either input backend).
    /// One order per game hour: whatever you queue last before the hour ticks is the one that is issued.
    ///   Left-click a division, then left-click an adjacent tile to move/attack.
    ///   Right-click / X: cancel the selected division's order.   Space: pause.   1-5: speed.   R: restart.
    /// </summary>
    public sealed class HumanController : MonoBehaviour, ITeamController
    {
        public MatchManager Match; public GameView View; public int Team;
        int queued = Cfg.NoopAction;
        static readonly float[] Speeds = { 2, 4, 8, 16, 32 };
        GUIStyle style;

        public int GetAction(GameSim sim, int team) { int a = queued; queued = Cfg.NoopAction; return a; }

        void OnGUI()
        {
            var e = Event.current;
            if (Match == null || Match.Sim == null) return;
            var sim = Match.Sim;
            float u = Screen.height / 720f;
            if (style == null) { style = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = false }; }
            style.fontSize = Mathf.RoundToInt(15 * u);

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Space) Match.Paused = !Match.Paused;
                else if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha5) Match.HoursPerSecond = Speeds[e.keyCode - KeyCode.Alpha1];
                else if (e.keyCode == KeyCode.R && sim.Done) { View.SelectedDiv = -1; Match.NewMatch(); }
                else if (e.keyCode == KeyCode.Escape) View.SelectedDiv = -1;
                else if (e.keyCode == KeyCode.X && Team >= 0) Cancel(sim);
            }
            if (e.type == EventType.MouseDown && Team >= 0)
            {
                if (e.button == 0) Click(sim, e.mousePosition);
                else if (e.button == 1) Cancel(sim);
            }

            DrawHud(sim, u);
        }

        Vector3 World(Vector2 guiPos)
        {
            var cam = Camera.main;
            return cam.ScreenToWorldPoint(new Vector3(guiPos.x, Screen.height - guiPos.y, -cam.transform.position.z));
        }

        void Click(GameSim sim, Vector2 guiPos)
        {
            Vector3 w = World(guiPos);
            // pick one of our own divisions?
            int pick = -1; float best = 0.42f;
            for (int i = 0; i < Cfg.DivsPerTeam; i++)
            {
                int id = Team * Cfg.DivsPerTeam + i;
                if (!sim.Divs[id].Alive) continue;
                float d = Vector2.Distance(View.DivPos(id), w);
                if (d < best) { best = d; pick = id; }
            }
            if (pick >= 0) { View.SelectedDiv = pick; return; }

            if (View.SelectedDiv < 0) return;
            var div = sim.Divs[View.SelectedDiv];
            int tile = View.TileAt(w);
            int dir = tile >= 0 ? Hex.DirTo(div.Tile, tile) : -1;
            if (dir < 0) { View.SelectedDiv = -1; return; }
            int action = div.Index * Cfg.CmdPerDiv + 1 + GameSim.ViewDir(Team, dir);
            if (sim.IsValidAction(Team, action)) queued = action;
        }

        void Cancel(GameSim sim)
        {
            if (Team < 0 || View.SelectedDiv < 0) return;
            int a = sim.Divs[View.SelectedDiv].Index * Cfg.CmdPerDiv;
            if (sim.IsValidAction(Team, a)) queued = a;
        }

        // ---------------------------------------------------------------- HUD
        void Text(float x, float y, float w, string s, Color c, float u, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            style.alignment = anchor;
            var r = new Rect(x, y, w, 24 * u);
            style.normal.textColor = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), s, style);
            style.normal.textColor = c;
            GUI.Label(r, s, style);
        }

        void DrawHud(GameSim sim, float u)
        {
            float m = 14 * u, line = 22 * u, W = Screen.width;
            Color white = new Color(0.92f, 0.94f, 0.98f), grey = new Color(0.8f, 0.85f, 0.95f), red = new Color(1f, 0.5f, 0.45f);

            int day = sim.Hour / 24 + 1, hr = sim.Hour % 24;
            string state = Match.Paused ? "  <b>PAUSED</b>" : "";
            Text(m, m, 400 * u, $"Day {Mathf.Min(day, Cfg.MaxDays)}/{Cfg.MaxDays}   {hr:00}:00   ({Match.HoursPerSecond:0}h/s){state}", white, u);

            for (int t = 0; t < 2; t++)
            {
                sim.CapturedVPs(t, out int op, out bool cap);
                string who = t == 0 ? "GREY" : "RED ";
                Text(m, m + line * (1.4f + t), 560 * u,
                    $"{who}  divisions {sim.AliveCount(t)}/14   enemy outposts {op}/4   enemy HQ {(cap ? "yes" : "no")}",
                    t == 0 ? grey : red, u);
            }

            // selected division
            if (View.SelectedDiv >= 0)
            {
                var d = sim.Divs[View.SelectedDiv];
                string order = d.Order < 0 ? "none" : (d.Attacking ? "ATTACK" : $"move {Mathf.RoundToInt(d.Progress * 100)}%");
                Text(m, m + line * 4f, 560 * u,
                    $"{d.Stats.Name} #{d.Index}   supply {d.Supply:0}/{d.Stats.MaxSupply:0}   org {d.Org:0}/{d.Stats.MaxOrg:0}   order: {order}",
                    white, u);
                Text(m, m + line * 5f, 560 * u,
                    $"soft {d.Stats.SoftAttack:0} hard {d.Stats.HardAttack:0} BT {d.Stats.Breakthrough:0} def {d.Stats.Defense:0} hardness {d.Stats.Hardness:0%}",
                    new Color(0.7f, 0.75f, 0.85f), u);
            }

            // event log
            for (int i = 0; i < sim.Log.Count; i++)
                Text(m, Screen.height - m - line * (sim.Log.Count - i), 600 * u, sim.Log[i], new Color(0.8f, 0.82f, 0.88f), u);

            // help
            string orderState = Team < 0 ? "spectating" : queued == Cfg.NoopAction ? "no order queued" : "<b>order queued</b>";
            Text(W - 420 * u - m, m, 420 * u, Team < 0 ? "Space pause · 1-5 speed · R restart" : "click unit → click adjacent tile", grey, u, TextAnchor.UpperRight);
            if (Team >= 0)
            {
                Text(W - 420 * u - m, m + line, 420 * u, "right-click/X cancel · Space pause · 1-5 speed", grey, u, TextAnchor.UpperRight);
                Text(W - 420 * u - m, m + line * 2, 420 * u, orderState + " (1 order / hour)", white, u, TextAnchor.UpperRight);
            }

            if (sim.Done)
            {
                string msg = sim.Winner == 2 ? "DRAW — day 60 reached"
                    : Team < 0 ? (sim.Winner == 0 ? "GREY WINS" : "RED WINS")
                    : (sim.Winner == Team ? "VICTORY" : "DEFEAT");
                style.fontSize = Mathf.RoundToInt(40 * u);
                Text(0, Screen.height * 0.45f, W, msg, white, u, TextAnchor.UpperCenter);
                style.fontSize = Mathf.RoundToInt(15 * u);
                Text(0, Screen.height * 0.45f + 56 * u, W, "press R to play again", grey, u, TextAnchor.UpperCenter);
            }
        }
    }
}
