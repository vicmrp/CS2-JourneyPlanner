using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace CS2_JourneyPlanner
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(CS2_JourneyPlanner)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            updateSystem.UpdateAt<JourneyPlannerUISystem>(
                SystemUpdatePhase.UIUpdate
            );

            log.Info("JourneyPlannerUISystem registered.");

        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
        }
    }
}
