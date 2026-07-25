using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace CS2_JourneyPlanner
{
    public class Mod : IMod
    {
        public static ILog Log = LogManager.GetLogger($"{nameof(CS2_JourneyPlanner)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                Log.Info($"Current mod asset at {asset.path}");

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
            Log.Info(nameof(OnDispose));
        }
    }
}
