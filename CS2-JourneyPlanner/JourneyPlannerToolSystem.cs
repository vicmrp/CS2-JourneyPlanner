using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2_JourneyPlanner
{
    public sealed partial class JourneyPlannerToolSystem : ToolBaseSystem
    {
        private JourneyPlannerUISystem _uiSystem;

        public override string toolID => "JourneyPlannerTool";

        public Entity StartOwner { get; private set; }
        public Entity DestinationOwner { get; private set; }

        public float3 StartPosition { get; private set; }
        public float3 DestinationPosition { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();

            _uiSystem =
                World.GetOrCreateSystemManaged<JourneyPlannerUISystem>();
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            Mod.log.Info("Journey planner tool activated.");
        }

        protected override void OnStopRunning()
        {
            base.OnStopRunning();
            Mod.log.Info("Journey planner tool deactivated.");
        }

        public override PrefabBase GetPrefab()
        {
            return null;
        }

        public override bool TrySetPrefab(PrefabBase prefab)
        {
            return false;
        }
    }
}