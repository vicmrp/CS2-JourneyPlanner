using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace CS2_JourneyPlanner
{
    public sealed partial class JourneyPlannerToolSystem
        : ToolBaseSystem
    {
        private JourneyPlannerUISystem _uiSystem;

        private bool _raycastInitializationLogged;

        public override string toolID =>
            "JourneyPlannerTool";

        protected override void OnCreate()
        {
            base.OnCreate();

            _uiSystem =
                World.GetOrCreateSystemManaged<
                    JourneyPlannerUISystem
                >();

            Mod.Log.Info(
                "JourneyPlannerToolSystem.OnCreate."
            );
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            applyAction.shouldBeEnabled = true;
            cancelAction.shouldBeEnabled = true;

            Mod.Log.Info(
                "Journey Planner tool activated."
            );
        }

        protected override void OnStopRunning()
        {
            applyAction.shouldBeEnabled = false;
            cancelAction.shouldBeEnabled = false;

            Mod.Log.Info(
                "Journey Planner tool deactivated."
            );

            base.OnStopRunning();
        }

        public override PrefabBase GetPrefab()
        {
            return null;
        }

        public override bool TrySetPrefab(
            PrefabBase prefab
        )
        {
            return false;
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();

            /*
             * Version 0.1c still uses broad masks.
             *
             * The raw click is inspected and validated after
             * the raycast result has been received.
             */
            m_ToolRaycastSystem.typeMask =
                unchecked((TypeMask)uint.MaxValue);

            m_ToolRaycastSystem.netLayerMask =
                unchecked((Layer)uint.MaxValue);

            if (_raycastInitializationLogged)
            {
                return;
            }

            _raycastInitializationLogged = true;

            Mod.Log.Info(
                "Journey Planner raycast initialized " +
                "with broad debug masks."
            );
        }

        protected override JobHandle OnUpdate(
            JobHandle inputDeps
        )
        {
            bool cancelPressed =
                cancelAction.WasPressedThisFrame();

            if (cancelPressed)
            {
                HandleCancel();

                return inputDeps;
            }

            bool applyPressed =
                applyAction.WasPressedThisFrame();

            if (!applyPressed)
            {
                return inputDeps;
            }

            ProcessMapClick();

            return inputDeps;
        }

        private void ProcessMapClick()
        {
            bool hasResult = GetRaycastResult(
                out Entity owner,
                out Game.Common.RaycastHit hit
            );

            Mod.Log.Info(
                $"Apply action pressed. " +
                $"HasRaycastResult={hasResult}"
            );

            if (!hasResult)
            {
                RejectPoint(
                    "No surface was found under the cursor."
                );

                return;
            }

            float3 position = hit.m_HitPosition;

            string entityType =
                GetEntityDescription(owner);

            LogSelectionDiagnostics(
                owner,
                position,
                entityType
            );

            if (
                !TryValidatePoint(
                    owner,
                    position,
                    out string rejectionReason
                )
            )
            {
                RejectPoint(rejectionReason);

                return;
            }

            Mod.Log.Info(
                $"Map point accepted. " +
                $"Owner={owner}, " +
                $"Type={entityType}, " +
                $"Position=({position.x:F1}, " +
                $"{position.y:F1}, " +
                $"{position.z:F1})"
            );

            _uiSystem.ConfirmSelection(
                owner,
                position,
                entityType
            );
        }

        private bool TryValidatePoint(
            Entity owner,
            float3 position,
            out string rejectionReason
        )
        {
            if (!math.all(math.isfinite(position)))
            {
                rejectionReason =
                    "The selected position contains invalid coordinates.";

                return false;
            }

            /*
             * A raycast may return Entity.Null when the terrain itself
             * is selected. Terrain points are allowed in version 0.1c.
             *
             * Network snapping will determine whether the point can
             * actually be used for pedestrian routing in a later version.
             */

            rejectionReason = string.Empty;

            return true;
        }

        private string GetEntityDescription(Entity entity)
        {
            if (entity == Entity.Null)
            {
                return "Terrain or unowned surface";
            }

            List<string> types = new List<string>();

            if (EntityManager.HasComponent<Node>(entity))
            {
                types.Add("Network node");
            }

            if (EntityManager.HasComponent<Edge>(entity))
            {
                types.Add("Network edge");
            }

            if (
                EntityManager.HasComponent<
                    Game.Buildings.Building
                >(entity)
            )
            {
                types.Add("Building");
            }

            if (
                EntityManager.HasComponent<
                    Game.Common.Owner
                >(entity)
            )
            {
                types.Add("Owned entity");
            }

            if (types.Count == 0)
            {
                return "Other map entity";
            }

            return string.Join(", ", types);
        }

        private void LogSelectionDiagnostics(
            Entity entity,
            float3 position,
            string entityType
        )
        {
            if (entity == Entity.Null)
            {
                Mod.Log.Info(
                    $"Selection diagnostics: " +
                    $"Owner=Entity.Null, " +
                    $"Type={entityType}, " +
                    $"Position=({position.x:F1}, " +
                    $"{position.y:F1}, " +
                    $"{position.z:F1})"
                );

                return;
            }

            bool hasNode =
                EntityManager.HasComponent<Node>(entity);

            bool hasEdge =
                EntityManager.HasComponent<Edge>(entity);

            bool hasBuilding =
                EntityManager.HasComponent<
                    Game.Buildings.Building
                >(entity);

            bool hasOwner =
                EntityManager.HasComponent<
                    Game.Common.Owner
                >(entity);

            Mod.Log.Info(
                $"Selection diagnostics: " +
                $"Owner={entity}, " +
                $"Type={entityType}, " +
                $"Node={hasNode}, " +
                $"Edge={hasEdge}, " +
                $"Building={hasBuilding}, " +
                $"OwnerComponent={hasOwner}, " +
                $"Position=({position.x:F1}, " +
                $"{position.y:F1}, " +
                $"{position.z:F1})"
            );
        }

        private void RejectPoint(string reason)
        {
            Mod.Log.Warn(
                $"Map point rejected. Reason={reason}"
            );

            _uiSystem.RejectSelection(reason);
        }

        private void HandleCancel()
        {
            Mod.Log.Info("Cancel action pressed.");

            _uiSystem.CancelSelection();

            ReturnToDefaultTool();
        }

        public void ReturnToDefaultTool()
        {
            if (m_ToolSystem.activeTool != this)
            {
                return;
            }

            Mod.Log.Info(
                "Returning to DefaultToolSystem."
            );

            m_ToolSystem.activeTool =
                m_DefaultToolSystem;
        }
    }
}