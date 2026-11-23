using HexWar.Play;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace HexWar.Agents
{
    /// <summary>
    /// One ML-Agents agent per team, both sharing the behaviour name "Strategy" (so one network plays either
    /// side thanks to the 180° perspective flip in GameSim.FillObservation).
    ///   Observation : 1635 floats  (10x10x11 board | 14x19 own | 14x19 enemy | 3 globals)
    ///   Action      : one discrete branch of 99  = division(14) x command(7) + no-op, with invalid actions masked.
    ///   Reward      : zero-sum game events from GameSim, multiplied by RewardScale for PPO stability.
    /// </summary>
    public sealed class StrategyAgent : Agent
    {
        public MatchManager Match;
        public int Team;
        public float RewardScale = 0.01f;     // +1000 victory -> +10
        readonly float[] obs = new float[Obs.Size];
        ScriptedPolicy heuristic;

        public void Bind(MatchManager m, int team)
        {
            Match = m; Team = team;
            heuristic = new ScriptedPolicy(team + 100);
            m.Stepped += OnStepped;
            m.ExternalRequested += OnRequested;
        }

        void OnRequested(int team) { if (team == Team) RequestDecision(); }   // realtime (non-training) pacing only

        void OnStepped(StepResult r)
        {
            AddReward((Team == 0 ? r.Reward0 : -r.Reward0) * RewardScale);
            if (r.Done) EndEpisode();
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            Match.Sim.FillObservation(Team, obs);
            sensor.AddObservation(obs);
        }

        public override void WriteDiscreteActionMask(IDiscreteActionMask mask)
        {
            var sim = Match.Sim;
            for (int a = 0; a < Cfg.ActionCount; a++)
                if (!sim.IsValidAction(Team, a)) mask.SetActionEnabled(0, a, false);
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            Match.SubmitAction(Team, actions.DiscreteActions[0]);
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            actionsOut.DiscreteActions[0] = heuristic.Act(Match.Sim, Team);
        }

        void OnDestroy()
        {
            if (Match == null) return;
            Match.Stepped -= OnStepped; Match.ExternalRequested -= OnRequested;
        }
    }

    static class AgentRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            GameBootstrap.AgentFactory = Create;
            GameBootstrap.TrainingProbe = () => Academy.Instance.IsCommunicatorOn;
        }

        static GameObject Create(MatchManager match, int team, bool training, Object model)
        {
            var go = new GameObject(team == 0 ? "Agent_Grey" : "Agent_Red");
            go.SetActive(false);                                   // configure before the Agent initialises
            var agent = go.AddComponent<StrategyAgent>();          // [RequireComponent] adds BehaviorParameters
            var bp = go.GetComponent<BehaviorParameters>();
            bp.BehaviorName = "Strategy";
            bp.TeamId = team;                                      // self-play needs distinct team ids
            bp.BrainParameters.VectorObservationSize = Obs.Size;
            bp.BrainParameters.NumStackedVectorObservations = 1;
            bp.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(Cfg.ActionCount);

            if (model != null)   // property type is NNModel (ML-Agents 2.x) or ModelAsset (3.x): set via reflection
            {
                try { typeof(BehaviorParameters).GetProperty("Model")?.SetValue(bp, model); } catch { }
                bp.BehaviorType = training ? BehaviorType.Default : BehaviorType.InferenceOnly;
            }
            else bp.BehaviorType = training ? BehaviorType.Default : BehaviorType.HeuristicOnly;

            if (training)
            {
                var dr = go.AddComponent<DecisionRequester>();
                dr.DecisionPeriod = 1; dr.TakeActionsBetweenDecisions = false;
            }
            agent.Bind(match, team);
            go.SetActive(true);
            return go;
        }
    }
}
