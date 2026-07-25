using System;
using System.Linq;
using System.Reflection;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
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
        private bool _nameSystemMethodsLogged;

        /*
         * Road currently shown with the blue hover outline.
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

            _highlightedEntity = Entity.Null;

            Mod.Log.Info(
                "JourneyPlannerToolSystem.OnCreate."
            );

            LogNameSystemMethods();
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
             * Restrict the raycast to road-network entities.
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
                "Journey Planner raycast initialized for roads only."
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

            if (!applyAction.WasPressedThisFrame())
            {
                return inputDeps;
            }

            ProcessMapClick();

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
                out Entity entity,
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

            LogRoadDiagnostics(
                entity,
                position
            );

            if (
                !TryValidateRoad(
                    entity,
                    position,
                    out string rejectionReason
                )
            )
            {
                RejectPoint(rejectionReason);
                return;
            }

            /*
             * Print all components on the selected road edge.
             */
            LogAllComponents(
                entity,
                "Selected road edge"
            );

            LogPossibleNamingComponents(
                entity
            );

            /*
             * Resolve:
             *
             * Road edge
             * → Game.Net.Aggregated
             * → aggregate road entity
             */
            Entity aggregateEntity =
                ResolveRoadAggregate(entity);

            if (aggregateEntity != Entity.Null)
            {
                Mod.Log.Info(
                    $"Resolved road aggregate: " +
                    $"RoadEdge={entity}, " +
                    $"Aggregate={aggregateEntity}"
                );

                LogAllComponents(
                    aggregateEntity,
                    "Road aggregate"
                );

                LogPossibleNamingComponents(
                    aggregateEntity
                );
            }
            else
            {
                Mod.Log.Warn(
                    $"Could not resolve aggregate entity " +
                    $"for road edge {entity}."
                );
            }

            Mod.Log.Info(
                $"Road-name diagnostic completed. " +
                $"Edge={entity}, " +
                $"Aggregate={aggregateEntity}"
            );

            Mod.Log.Info(
                $"Road selected. " +
                $"Entity={entity}, " +
                $"Position=({position.x:F1}, " +
                $"{position.y:F1}, " +
                $"{position.z:F1})"
            );

            _uiSystem.ConfirmRoadSelection(
                entity,
                position
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
                    "The selected network is not a road.";

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
                EntityManager.HasComponent<
                    Edge
                >(entity) &&
                EntityManager.HasComponent<
                    Road
                >(entity);
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
                Mod.Log.Warn(
                    "Cannot resolve aggregate: " +
                    "the road edge is invalid."
                );

                return Entity.Null;
            }

            if (
                !EntityManager.HasComponent<
                    Aggregated
                >(roadEdge)
            )
            {
                Mod.Log.Warn(
                    $"Road edge {roadEdge} does not contain " +
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

            Mod.Log.Info(
                $"Aggregated component: " +
                $"RoadEdge={roadEdge}, " +
                $"m_Aggregate={aggregateEntity}"
            );

            if (
                aggregateEntity == Entity.Null ||
                !EntityManager.Exists(aggregateEntity)
            )
            {
                Mod.Log.Warn(
                    $"Aggregate entity {aggregateEntity} " +
                    "is null or does not exist."
                );

                return Entity.Null;
            }

            return aggregateEntity;
        }

        private void LogRoadDiagnostics(
            Entity entity,
            float3 position
        )
        {
            if (
                entity == Entity.Null ||
                !EntityManager.Exists(entity)
            )
            {
                Mod.Log.Info(
                    $"Road diagnostics: " +
                    $"Entity=Entity.Null, " +
                    $"Position=({position.x:F1}, " +
                    $"{position.y:F1}, " +
                    $"{position.z:F1})"
                );

                return;
            }

            bool hasEdge =
                EntityManager.HasComponent<
                    Edge
                >(entity);

            bool hasNode =
                EntityManager.HasComponent<
                    Node
                >(entity);

            bool hasRoad =
                EntityManager.HasComponent<
                    Road
                >(entity);

            bool hasAggregated =
                EntityManager.HasComponent<
                    Aggregated
                >(entity);

            bool isHighlighted =
                EntityManager.HasComponent<
                    Highlighted
                >(entity);

            Mod.Log.Info(
                $"Road diagnostics: " +
                $"Entity={entity}, " +
                $"Edge={hasEdge}, " +
                $"Node={hasNode}, " +
                $"Road={hasRoad}, " +
                $"Aggregated={hasAggregated}, " +
                $"Highlighted={isHighlighted}, " +
                $"Position=({position.x:F1}, " +
                $"{position.y:F1}, " +
                $"{position.z:F1})"
            );
        }

        private void LogAllComponents(
            Entity entity,
            string label
        )
        {
            if (
                entity == Entity.Null ||
                !EntityManager.Exists(entity)
            )
            {
                Mod.Log.Warn(
                    $"{label}: entity is null " +
                    "or no longer exists."
                );

                return;
            }

            using (
                NativeArray<ComponentType> componentTypes =
                    EntityManager.GetComponentTypes(
                        entity,
                        Allocator.Temp
                    )
            )
            {
                Mod.Log.Info(
                    $"{label}: {entity} contains " +
                    $"{componentTypes.Length} components."
                );

                for (
                    int index = 0;
                    index < componentTypes.Length;
                    index++
                )
                {
                    ComponentType componentType =
                        componentTypes[index];

                    Type managedType =
                        componentType.GetManagedType();

                    string typeName =
                        managedType != null
                            ? managedType.FullName
                            : componentType.ToString();

                    Mod.Log.Info(
                        $"  Component[{index}]={typeName}"
                    );
                }
            }
        }

        private void LogPossibleNamingComponents(
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

            using (
                NativeArray<ComponentType> componentTypes =
                    EntityManager.GetComponentTypes(
                        entity,
                        Allocator.Temp
                    )
            )
            {
                bool foundCandidate = false;

                Mod.Log.Info(
                    $"Searching {entity} for possible " +
                    "road-name and aggregate components."
                );

                for (
                    int index = 0;
                    index < componentTypes.Length;
                    index++
                )
                {
                    ComponentType componentType =
                        componentTypes[index];

                    Type managedType =
                        componentType.GetManagedType();

                    string typeName =
                        managedType != null
                            ? managedType.FullName
                            : componentType.ToString();

                    if (
                        ContainsIgnoreCase(
                            typeName,
                            "name"
                        ) ||
                        ContainsIgnoreCase(
                            typeName,
                            "localization"
                        ) ||
                        ContainsIgnoreCase(
                            typeName,
                            "aggregat"
                        ) ||
                        ContainsIgnoreCase(
                            typeName,
                            "owner"
                        ) ||
                        ContainsIgnoreCase(
                            typeName,
                            "label"
                        )
                    )
                    {
                        foundCandidate = true;

                        Mod.Log.Info(
                            $"  Possible road-name component: " +
                            $"{typeName}"
                        );
                    }
                }

                if (!foundCandidate)
                {
                    Mod.Log.Warn(
                        "No obvious Name, Localization, " +
                        "Aggregate, Owner or Label component " +
                        "was found on the entity."
                    );
                }
            }
        }

        private void LogNameSystemMethods()
        {
            if (_nameSystemMethodsLogged)
            {
                return;
            }

            _nameSystemMethodsLogged = true;

            try
            {
                Type nameSystemType =
                    typeof(Game.UI.NameSystem);

                MethodInfo[] methods =
                    nameSystemType.GetMethods(
                        BindingFlags.Instance |
                        BindingFlags.Static |
                        BindingFlags.Public |
                        BindingFlags.NonPublic
                    );

                Mod.Log.Info(
                    $"Game.UI.NameSystem contains " +
                    $"{methods.Length} methods."
                );

                MethodInfo[] nameMethods =
                    methods
                        .Where(method =>
                            method.Name.IndexOf(
                                "name",
                                StringComparison.OrdinalIgnoreCase
                            ) >= 0)
                        .OrderBy(method => method.Name)
                        .ThenBy(method =>
                            method.GetParameters().Length)
                        .ToArray();

                Mod.Log.Info(
                    $"Game.UI.NameSystem contains " +
                    $"{nameMethods.Length} name-related methods."
                );

                foreach (MethodInfo method in nameMethods)
                {
                    ParameterInfo[] methodParameters =
                        method.GetParameters();

                    string parameters =
                        string.Join(
                            ", ",
                            methodParameters.Select(
                                parameter =>
                                    FormatParameter(parameter)
                            )
                        );

                    Mod.Log.Info(
                        $"NameSystem method: " +
                        $"{GetReadableTypeName(method.ReturnType)} " +
                        $"{method.Name}({parameters})"
                    );
                }
            }
            catch (Exception exception)
            {
                Mod.Log.Error(
                    $"Failed to inspect Game.UI.NameSystem. " +
                    $"{exception}"
                );
            }
        }

        private static string FormatParameter(
            ParameterInfo parameter
        )
        {
            string prefix = string.Empty;

            if (parameter.IsOut)
            {
                prefix = "out ";
            }
            else if (
                parameter.ParameterType.IsByRef
            )
            {
                prefix = "ref ";
            }

            Type parameterType =
                parameter.ParameterType;

            if (parameterType.IsByRef)
            {
                parameterType =
                    parameterType.GetElementType();
            }

            return
                $"{prefix}" +
                $"{GetReadableTypeName(parameterType)} " +
                $"{parameter.Name}";
        }

        private static string GetReadableTypeName(
            Type type
        )
        {
            if (type == null)
            {
                return "unknown";
            }

            if (!type.IsGenericType)
            {
                return type.FullName ?? type.Name;
            }

            string genericName =
                type.GetGenericTypeDefinition().FullName;

            int backtickIndex =
                genericName.IndexOf('`');

            if (backtickIndex >= 0)
            {
                genericName =
                    genericName.Substring(
                        0,
                        backtickIndex
                    );
            }

            string arguments =
                string.Join(
                    ", ",
                    type.GetGenericArguments()
                        .Select(GetReadableTypeName)
                );

            return
                $"{genericName}<{arguments}>";
        }

        private static bool ContainsIgnoreCase(
            string source,
            string searchValue
        )
        {
            if (
                string.IsNullOrEmpty(source) ||
                string.IsNullOrEmpty(searchValue)
            )
            {
                return false;
            }

            return source.IndexOf(
                searchValue,
                StringComparison.OrdinalIgnoreCase
            ) >= 0;
        }

        private void RejectPoint(
            string reason
        )
        {
            Mod.Log.Warn(
                $"Road selection failed. " +
                $"Reason={reason}"
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