using Colossal.Logging;
using Game;
using Game.Modding;

namespace CS2_JourneyPlanner
{
    public sealed class Mod : IMod
    {
        public const string Version = "0.2a";
        public const string VersionName = "Window Toggle";

        public static readonly ILog Log = LogManager
            .GetLogger(nameof(CS2_JourneyPlanner))
            .SetShowsErrorsInUI(true);

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info(
                $"Journey Planner v{Version} " +
                $"({VersionName}) loading."
            );

            Log.Info(
                "Journey Planner systems are being registered."
            );

            updateSystem.UpdateAt<
                JourneyPlannerUISystem
            >(
                SystemUpdatePhase.UIUpdate
            );

            updateSystem.UpdateAt<
                JourneyPlannerToolSystem
            >(
                SystemUpdatePhase.ToolUpdate
            );

            Log.Info(
                "Journey Planner systems registered."
            );
        }

        public void OnDispose()
        {
            Log.Info(
                $"Journey Planner v{Version} disposed."
            );
        }
    }
}

