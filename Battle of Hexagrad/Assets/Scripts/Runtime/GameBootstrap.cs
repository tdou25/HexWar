using System;
using UnityEngine;

namespace HexWar.Play
{
    public enum PlayMode
    {
        HumanVsScripted,   // you are Grey
        HumanVsAgent,      // you are Grey, a trained policy (or heuristic) is Red
        AgentVsScripted,   // watch a policy play the scripted bot
        ScriptedVsScripted,
        PolicyVsScripted,  // Grey = network exported from python/ (StreamingAssets/policy.bytes), Red = scripted bot
        PolicyVsHuman,     // Grey = exported network, you are Red
        TrainSelfPlay,     // ML-Agents: both teams are agents (shared behaviour "Strategy"), no real-time pacing
        TrainVsScripted,   // ML-Agents: Grey is the agent, Red is the scripted bot
    }

    /// <summary>
    /// Drop-in entry point. If the scene doesn't contain one, an instance is created automatically,
    /// so an EMPTY scene is enough to run the game. Command line: -mode TrainSelfPlay -seed 7
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        /// <summary>Set by Game.Agents (only compiled when ML-Agents is installed).</summary>
        public static Func<MatchManager, int, bool, UnityEngine.Object, GameObject> AgentFactory;
        public static Func<bool> TrainingProbe;

        public PlayMode Mode = PlayMode.HumanVsScripted;
        public int Seed = 1;
        [Tooltip("Exported policy file; relative to Assets/StreamingAssets or absolute. Used by the Policy* modes.")]
        public string PolicyFile = "policy.bytes";
        public float HoursPerSecond = 4f;
        [Tooltip("Optional trained .onnx for agent-controlled teams (inference).")]
        public UnityEngine.Object PolicyModel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Auto()
        {
#if UNITY_2023_1_OR_NEWER
            if (FindAnyObjectByType<GameBootstrap>() == null)
#else
            if (FindObjectOfType<GameBootstrap>() == null)
#endif
                new GameObject("HexWar").AddComponent<GameBootstrap>();
        }

        void Start()
        {
            ParseArgs();
            bool trainerAttached = AgentFactory != null && TrainingProbe != null && TrainingProbe();
            if (trainerAttached && Mode != PlayMode.TrainVsScripted) Mode = PlayMode.TrainSelfPlay;
            bool training = Mode == PlayMode.TrainSelfPlay || Mode == PlayMode.TrainVsScripted;
            bool headless = Application.isBatchMode;

            var match = gameObject.AddComponent<MatchManager>();
            match.Seed = Seed; match.HoursPerSecond = HoursPerSecond;
            match.NewMatch();                       // (re)create the sim with the chosen seed
            match.Realtime = !training; match.AutoRestart = training;

            GameView view = null;
            if (!headless)
            {
                SetupCamera();
                view = gameObject.AddComponent<GameView>(); view.Match = match;
            }

            PolicyController policy = null;
            if (Mode == PlayMode.PolicyVsScripted || Mode == PlayMode.PolicyVsHuman) policy = PolicyController.Load(PolicyFile);

            bool spectate = !headless && !training && Mode != PlayMode.HumanVsScripted && Mode != PlayMode.HumanVsAgent && Mode != PlayMode.PolicyVsHuman;
            if (spectate)   // HUD + pause/speed/restart only (Team -1 = no orders)
            {
                var sp = gameObject.AddComponent<HumanController>(); sp.Match = match; sp.View = view; sp.Team = -1;
            }

            for (int team = 0; team < 2; team++)
            {
                bool human = !headless && ((team == 0 && (Mode == PlayMode.HumanVsScripted || Mode == PlayMode.HumanVsAgent))
                                           || (team == 1 && Mode == PlayMode.PolicyVsHuman));
                bool agent = Mode == PlayMode.TrainSelfPlay || (team == 0 && (Mode == PlayMode.AgentVsScripted || Mode == PlayMode.TrainVsScripted))
                             || (team == 1 && Mode == PlayMode.HumanVsAgent);
                if (agent && AgentFactory == null)
                {
                    Debug.LogWarning("HexWar: ML-Agents package not found — falling back to the scripted bot for team " + team);
                    agent = false;
                }
                if (team == 0 && policy != null) match.Controllers[team] = policy;
                else if (agent) { match.External[team] = true; AgentFactory(match, team, training, PolicyModel); }
                else if (human)
                {
                    var hc = gameObject.AddComponent<HumanController>();
                    hc.Match = match; hc.View = view; hc.Team = team; match.Controllers[team] = hc;
                }
                else match.Controllers[team] = new ScriptedController(Seed * 31 + team);
            }
        }

        void ParseArgs()
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
            {
                if (a[i] == "-mode" && Enum.TryParse(a[i + 1], true, out PlayMode m)) Mode = m;
                if (a[i] == "-policy") PolicyFile = a[i + 1];
                if (a[i] == "-seed" && int.TryParse(a[i + 1], out int s)) Seed = s;
            }
        }

        static void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null) { var go = new GameObject("Main Camera") { tag = "MainCamera" }; cam = go.AddComponent<Camera>(); }
            cam.orthographic = true; cam.orthographicSize = 10.2f;
            cam.transform.position = new Vector3(0, 0, -10);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.09f);
        }
    }
}
