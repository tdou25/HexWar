using UnityEngine;

namespace HexWar.Play
{
    /// <summary>Plays a team with a network exported from python/ (no ML-Agents / ONNX needed).</summary>
    public sealed class PolicyController : ITeamController
    {
        readonly MlpPolicy net;
        public PolicyController(MlpPolicy net, bool greedy = true) { this.net = net; net.Greedy = greedy; }
        public int GetAction(GameSim sim, int team) => net.Act(sim, team);

        /// <summary>path is absolute, or relative to Assets/StreamingAssets. Returns null (and logs) on failure.</summary>
        public static PolicyController Load(string path)
        {
            if (!System.IO.Path.IsPathRooted(path)) path = System.IO.Path.Combine(Application.streamingAssetsPath, path);
            try { return new PolicyController(MlpPolicy.Load(System.IO.File.ReadAllBytes(path))); }
            catch (System.Exception e) { Debug.LogError($"HexWar: couldn't load policy '{path}': {e.Message}"); return null; }
        }
    }
}
