using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace CS2_JourneyPlanner
{
    public sealed class Mod : IMod
    {
        public const string Version = "0.1c";

        public static readonly ILog Log = LogManager
            .GetLogger($"{nameof(CS2_JourneyPlanner)}.{nameof(Mod)}")
            .SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info($"Journey Planner v{Version} loading.");

            if (
                GameManager.instance.modManager.TryGetExecutableAsset(
                    this,
                    out var asset
                )
            )
            {
                Log.Info($"Current mod asset at {asset.path}");
            }

            updateSystem.UpdateAt<JourneyPlannerUISystem>(
                SystemUpdatePhase.UIUpdate
            );

            updateSystem.UpdateAt<JourneyPlannerToolSystem>(
                SystemUpdatePhase.ToolUpdate
            );

            Log.Info("Journey Planner systems registered.");
        }

        public void OnDispose()
        {
            Log.Info($"Journey Planner v{Version} disposed.");
        }
    }
}