using System;
using UnityEngine;

namespace HexWar.Play
{
    public interface ITeamController { int GetAction(GameSim sim, int team); }

    public sealed class ScriptedController : ITeamController
    {
        readonly ScriptedPolicy policy;
        public ScriptedController(int seed) { policy = new ScriptedPolicy(seed); }
        public int GetAction(GameSim sim, int team) => policy.Act(sim, team);
    }

    /// <summary>
    /// Owns the GameSim and decides when an in-game hour elapses.
    ///  - Realtime: a timer ticks the sim (human play / spectating); "external" teams (RL agents) are asked
    ///    for a decision each hour and the hour resolves once they answer.
    ///  - Non-realtime (training): the agents' decisions drive the sim, one hour per Academy step.
    /// </summary>
    public sealed class MatchManager : MonoBehaviour
    {
        public GameSim Sim { get; private set; }
        public readonly ITeamController[] Controllers = new ITeamController[2];
        public readonly bool[] External = new bool[2];
        public bool Realtime = true, Paused, AutoRestart;
        public float HoursPerSecond = 4f;
        public int Seed = 1;
        public StepResult LastResult;

        public event Action<StepResult> Stepped;     // fired after every hour (agents collect rewards here)
        public event Action<int> ExternalRequested;  // realtime: "team X, please decide now"
        public event Action MatchReset;

        float timer;
        bool awaiting;
        readonly int[] submitted = { -1, -1 };

        void Awake() { Sim = new GameSim(Seed); }

        public void NewMatch()
        {
            Seed++;
            Sim = new GameSim(Seed);
            submitted[0] = submitted[1] = -1; awaiting = false; timer = 0;
            MatchReset?.Invoke();
        }

        void Update()
        {
            if (!Realtime || Paused || Sim.Done || awaiting) return;
            timer += Time.deltaTime * HoursPerSecond;
            for (int n = 0; n < 8 && timer >= 1f && !Sim.Done && !awaiting; n++) { timer -= 1f; BeginHour(); }
            if (timer > 2f) timer = 2f;
        }

        void BeginHour()
        {
            if (!External[0] && !External[1]) { TryStep(); return; }
            awaiting = true;
            for (int t = 0; t < 2; t++) if (External[t]) ExternalRequested?.Invoke(t);
        }

        /// <summary>Called by RL agents (or anything external) with their one action for this hour.</summary>
        public void SubmitAction(int team, int action) { submitted[team] = action; TryStep(); }

        void TryStep()
        {
            for (int t = 0; t < 2; t++) if (External[t] && submitted[t] < 0) return;
            int a0 = External[0] ? submitted[0] : Controllers[0].GetAction(Sim, 0);
            int a1 = External[1] ? submitted[1] : Controllers[1].GetAction(Sim, 1);
            submitted[0] = submitted[1] = -1; awaiting = false;

            LastResult = Sim.Tick(a0, a1);
            Stepped?.Invoke(LastResult);
            if (LastResult.Done && AutoRestart) NewMatch();
        }
    }
}
