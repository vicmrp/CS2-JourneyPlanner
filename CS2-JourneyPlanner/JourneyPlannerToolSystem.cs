using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using Unity.Jobs;

namespace CS2_JourneyPlanner
{
    public sealed partial class JourneyPlannerToolSystem
        : ToolBaseSystem
    {
        private JourneyPlannerUISystem _uiSystem;

        private Entity _lastHoveredEntity;
        private int _updateCount;
        private bool _raycastInitializationLogged;

        public override string toolID => "JourneyPlannerTool";

        protected override void OnCreate()
        {
            base.OnCreate();

            _uiSystem =
                World.GetOrCreateSystemManaged<JourneyPlannerUISystem>();

            _lastHoveredEntity = Entity.Null;
            _updateCount = 0;

            Mod.Log.Info("JourneyPlannerToolSystem.OnCreate.");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            applyAction.shouldBeEnabled = true;
            cancelAction.shouldBeEnabled = true;

            _updateCount = 0;
            _lastHoveredEntity = Entity.Null;

            Mod.Log.Info(
                "Journey Planner tool activated. " +
                "Apply and cancel actions enabled."
            );
        }

        protected override void OnStopRunning()
        {
            Mod.Log.Info("Journey Planner tool deactivated.");

            _lastHoveredEntity = Entity.Null;

            base.OnStopRunning();
        }

        public override PrefabBase GetPrefab()
        {
            return null;
        }

        public override bool TrySetPrefab(PrefabBase prefab)
        {
            return false;
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();

            /*
             * Debug configuration:
             * accept every TypeMask and every network layer.
             *
             * Later we will replace this with road-specific masks.
             */
            m_ToolRaycastSystem.typeMask = unchecked((TypeMask)(-1));
            m_ToolRaycastSystem.netLayerMask = unchecked((Layer)(-1));

            if (!_raycastInitializationLogged)
            {
                _raycastInitializationLogged = true;

                Mod.Log.Info(
                    "Journey Planner raycast initialized with broad debug masks."
                );
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            _updateCount++;

            bool applyPressed =
                applyAction.WasPressedThisFrame();

            bool cancelPressed =
                cancelAction.WasPressedThisFrame();

            bool hasResult = GetRaycastResult(
                out Entity owner,
                out Game.Common.RaycastHit hit
            );

            /*
             * Report that the system is actually updating.
             * Approximately once every 300 updates.
             */
            if (_updateCount % 300 == 0)
            {
                Mod.Log.Info(
                    $"Journey tool update heartbeat. " +
                    $"Updates={_updateCount}, " +
                    $"HasRaycastResult={hasResult}"
                );
            }

            if (hasResult)
            {
                var position = hit.m_HitPosition;

                if (owner != _lastHoveredEntity)
                {
                    _lastHoveredEntity = owner;

                    Mod.Log.Info(
                        $"Hover result changed. " +
                        $"Owner={owner}, " +
                        $"Position=({position.x:F1}, " +
                        $"{position.y:F1}, " +
                        $"{position.z:F1})"
                    );
                }
            }

            if (applyPressed)
            {
                Mod.Log.Info(
                    $"Apply action pressed. " +
                    $"HasRaycastResult={hasResult}"
                );

                if (!hasResult)
                {
                    Mod.Log.Warn(
                        "Map click was detected, but the raycast " +
                        "did not return a valid point."
                    );
                }
                else
                {
                    var position = hit.m_HitPosition;

                    Mod.Log.Info(
                        $"Map point selected. " +
                        $"Owner={owner}, " +
                        $"Position=({position.x:F1}, " +
                        $"{position.y:F1}, " +
                        $"{position.z:F1})"
                    );

                    _uiSystem.ConfirmSelection(
                        owner,
                        position
                    );
                }
            }

            if (cancelPressed)
            {
                Mod.Log.Info(
                    "Cancel action pressed."
                );

                _uiSystem.CancelSelection();
                ReturnToDefaultTool();
            }

            return inputDeps;
        }

        public void ReturnToDefaultTool()
        {
            if (m_ToolSystem.activeTool != this)
            {
                Mod.Log.Info(
                    "ReturnToDefaultTool ignored because " +
                    "Journey Planner is not active."
                );

                return;
            }

            Mod.Log.Info("Returning to DefaultToolSystem.");

            m_ToolSystem.activeTool =
                m_DefaultToolSystem;
        }
    }
}