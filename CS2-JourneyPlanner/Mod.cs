using Colossal.Logging;
using Game;
using Game.Modding;

namespace CS2_JourneyPlanner
{
    public sealed class Mod : IMod
    {
        public const string Version = "1.2.0";

        public static readonly ILog Log = LogManager
            .GetLogger(nameof(CS2_JourneyPlanner))
            .SetShowsErrorsInUI(true);

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info($"Journey Planner {Version} loading.");
            updateSystem.UpdateAt<JourneyPlannerUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<JourneyPlannerToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<JourneyPlannerRouteSystem>(SystemUpdatePhase.Rendering);
            Log.Info("Journey Planner registered.");
        }

        public void OnDispose()
        {
            Log.Info($"Journey Planner {Version} disposed.");
        }
    }
}
