using System;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Game.UI;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace CS2_JourneyPlanner
{
    public sealed partial class JourneyPlannerToolSystem
        : ToolBaseSystem
    {
        private JourneyPlannerUISystem _uiSystem;
        private NameSystem _nameSystem;

        private bool _raycastInitializationLogged;

        /*
         * The road edge currently receiving the blue
         * hover highlight.
         */
        private Entity _highlightedEntity;

        public override string toolID =>
            "JourneyPlannerTool";

        protected override void OnCreate()
        {
            base.OnCreate();

            _uiSystem =
                World.GetOrCreateSystemManaged<
                    JourneyPlannerUISystem
                >();

            _nameSystem =
                World.GetOrCreateSystemManaged<
                    NameSystem
                >();

            _highlightedEntity = Entity.Null;

            Mod.Log.Info(
                "JourneyPlannerToolSystem.OnCreate."
            );
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            applyAction.shouldBeEnabled = true;
            cancelAction.shouldBeEnabled = true;

            _highlightedEntity = Entity.Null;

            Mod.Log.Info(
                "Journey Planner tool activated."
            );
        }

        protected override void OnStopRunning()
        {
            ClearHoverHighlight();

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
             * Only road-network entities should be returned.
             */
            m_ToolRaycastSystem.typeMask =
                TypeMask.Net;

            m_ToolRaycastSystem.netLayerMask =
                Layer.Road;

            if (_raycastInitializationLogged)
            {
                return;
            }

            _raycastInitializationLogged = true;

            Mod.Log.Info(
                "Journey Planner raycast initialized " +
                "for roads only."
            );
        }

        protected override JobHandle OnUpdate(
            JobHandle inputDeps
        )
        {
            UpdateHoverHighlight();

            if (cancelAction.WasPressedThisFrame())
            {
                HandleCancel();

                return inputDeps;
            }

            if (applyAction.WasPressedThisFrame())
            {
                ProcessMapClick();
            }

            return inputDeps;
        }

        private void UpdateHoverHighlight()
        {
            bool hasResult = GetRaycastResult(
                out Entity entity,
                out Game.Common.RaycastHit hit
            );

            if (
                !hasResult ||
                !IsRoadEdge(entity)
            )
            {
                ClearHoverHighlight();
                return;
            }

            if (entity == _highlightedEntity)
            {
                return;
            }

            ClearHoverHighlight();
            SetHoverHighlight(entity);
        }

        private void SetHoverHighlight(
            Entity entity
        )
        {
            if (
                entity == Entity.Null ||
                !EntityManager.Exists(entity)
            )
            {
                return;
            }

            if (
                !EntityManager.HasComponent<
                    Highlighted
                >(entity)
            )
            {
                EntityManager.AddComponent<
                    Highlighted
                >(entity);
            }

            _highlightedEntity = entity;
        }

        private void ClearHoverHighlight()
        {
            if (_highlightedEntity == Entity.Null)
            {
                return;
            }

            if (
                EntityManager.Exists(
                    _highlightedEntity
                ) &&
                EntityManager.HasComponent<
                    Highlighted
                >(_highlightedEntity)
            )
            {
                EntityManager.RemoveComponent<
                    Highlighted
                >(_highlightedEntity);
            }

            _highlightedEntity = Entity.Null;
        }

        private void ProcessMapClick()
        {
            bool hasResult = GetRaycastResult(
                out Entity roadEdge,
                out Game.Common.RaycastHit hit
            );

            Mod.Log.Info(
                $"Apply action pressed. " +
                $"HasRaycastResult={hasResult}"
            );

            if (!hasResult)
            {
                RejectPoint(
                    "No road was found under the cursor."
                );

                return;
            }

            float3 position =
                hit.m_HitPosition;

            if (
                !TryValidateRoad(
                    roadEdge,
                    position,
                    out string rejectionReason
                )
            )
            {
                RejectPoint(rejectionReason);
                return;
            }

            Entity aggregateEntity =
                ResolveRoadAggregate(roadEdge);

            string roadName =
                ResolveRoadName(
                    roadEdge,
                    aggregateEntity
                );

            Mod.Log.Info(
                $"Road selected. " +
                $"Edge={roadEdge}, " +
                $"Aggregate={aggregateEntity}, " +
                $"Name={roadName}, " +
                $"Position=({position.x:F1}, " +
                $"{position.y:F1}, " +
                $"{position.z:F1})"
            );

            _uiSystem.ConfirmRoadSelection(
                roadEdge,
                aggregateEntity,
                position,
                roadName
            );
        }

        private bool TryValidateRoad(
            Entity entity,
            float3 position,
            out string rejectionReason
        )
        {
            if (!math.all(math.isfinite(position)))
            {
                rejectionReason =
                    "The selected point has invalid coordinates.";

                return false;
            }

            if (
                entity == Entity.Null ||
                !EntityManager.Exists(entity)
            )
            {
                rejectionReason =
                    "No road was found under the cursor.";

                return false;
            }

            if (!IsRoadEdge(entity))
            {
                rejectionReason =
                    "The selected network entity is not a road.";

                return false;
            }

            rejectionReason = string.Empty;

            return true;
        }

        private bool IsRoadEdge(
            Entity entity
        )
        {
            if (
                entity == Entity.Null ||
                !EntityManager.Exists(entity)
            )
            {
                return false;
            }

            return
                EntityManager.HasComponent<Edge>(entity) &&
                EntityManager.HasComponent<Road>(entity);
        }

        private Entity ResolveRoadAggregate(
            Entity roadEdge
        )
        {
            if (
                roadEdge == Entity.Null ||
                !EntityManager.Exists(roadEdge)
            )
            {
                return Entity.Null;
            }

            if (
                !EntityManager.HasComponent<
                    Aggregated
                >(roadEdge)
            )
            {
                Mod.Log.Warn(
                    $"Road edge {roadEdge} does not have " +
                    "Game.Net.Aggregated."
                );

                return Entity.Null;
            }

            Aggregated aggregated =
                EntityManager.GetComponentData<
                    Aggregated
                >(roadEdge);

            Entity aggregateEntity =
                aggregated.m_Aggregate;

            if (
                aggregateEntity == Entity.Null ||
                !EntityManager.Exists(aggregateEntity)
            )
            {
                Mod.Log.Warn(
                    $"Road aggregate {aggregateEntity} " +
                    $"for edge {roadEdge} is invalid."
                );

                return Entity.Null;
            }

            return aggregateEntity;
        }

        private string ResolveRoadName(
            Entity roadEdge,
            Entity aggregateEntity
        )
        {
            /*
             * The rendered road label belongs to the aggregate,
             * not to the individual road edge.
             */
            Entity namingEntity =
                aggregateEntity != Entity.Null
                    ? aggregateEntity
                    : roadEdge;

            try
            {
                string roadName =
                    _nameSystem.GetRenderedLabelName(
                        namingEntity
                    );

                if (!string.IsNullOrWhiteSpace(roadName))
                {
                    Mod.Log.Info(
                        $"Resolved road name. " +
                        $"Entity={namingEntity}, " +
                        $"Name={roadName}"
                    );

                    return roadName;
                }

                /*
                 * GetRenderedLabelName should normally return the
                 * generated or custom road name. This fallback
                 * checks explicitly for a custom name.
                 */
                if (
                    _nameSystem.TryGetCustomName(
                        namingEntity,
                        out string customName
                    ) &&
                    !string.IsNullOrWhiteSpace(customName)
                )
                {
                    Mod.Log.Info(
                        $"Resolved custom road name. " +
                        $"Entity={namingEntity}, " +
                        $"Name={customName}"
                    );

                    return customName;
                }
            }
            catch (Exception exception)
            {
                Mod.Log.Error(
                    $"Failed to resolve road name. " +
                    $"RoadEdge={roadEdge}, " +
                    $"Aggregate={aggregateEntity}, " +
                    $"Exception={exception}"
                );
            }

            Mod.Log.Warn(
                $"No road name was resolved. " +
                $"RoadEdge={roadEdge}, " +
                $"Aggregate={aggregateEntity}"
            );

            return "Unnamed road";
        }

        private void RejectPoint(
            string reason
        )
        {
            Mod.Log.Warn(
                $"Road selection failed. Reason={reason}"
            );

            _uiSystem.RejectSelection(reason);
        }

        private void HandleCancel()
        {
            Mod.Log.Info(
                "Cancel action pressed."
            );

            ClearHoverHighlight();

            _uiSystem.CancelSelection();

            ReturnToDefaultTool();
        }

        public void ReturnToDefaultTool()
        {
            if (m_ToolSystem.activeTool != this)
            {
                return;
            }

            ClearHoverHighlight();

            Mod.Log.Info(
                "Returning to DefaultToolSystem."
            );

            m_ToolSystem.activeTool =
                m_DefaultToolSystem;
        }
    }
}