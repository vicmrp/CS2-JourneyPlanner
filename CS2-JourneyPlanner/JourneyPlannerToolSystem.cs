using System;
using Game.Buildings;
using Game.Common;
using Game.Creatures;
using Game.Objects;
using Unity.Collections;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace CS2_JourneyPlanner
{
    public sealed partial class JourneyPlannerToolSystem : ToolBaseSystem
    {
        private JourneyPlannerUISystem _ui;
        private Entity _highlighted = Entity.Null;

        public override string toolID => "JourneyPlannerNativeTool";

        protected override void OnCreate()
        {
            base.OnCreate();
            _ui = World.GetOrCreateSystemManaged<JourneyPlannerUISystem>();
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            applyAction.shouldBeEnabled = true;
            cancelAction.shouldBeEnabled = true;
            InitializeRaycast();
        }

        protected override void OnStopRunning()
        {
            ClearHighlight();
            applyAction.shouldBeEnabled = false;
            cancelAction.shouldBeEnabled = false;
            base.OnStopRunning();
        }

        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            TypeMask mask = default;
            foreach (TypeMask value in Enum.GetValues(typeof(TypeMask)))
                mask |= value;
            m_ToolRaycastSystem.typeMask = mask;
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            bool hasResult = GetRaycastResult(out Entity hitEntity, out RaycastHit hit);

            Entity selectable = Entity.Null;
            if (hasResult)
            {
                // When JP is choosing a citizen/start point, citizens get first refusal.
                // This makes pedestrians selectable even when the native raycast lands on
                // a bus stop, tram platform, lane, shelter, or other surface beneath them.
                if (_ui.PreferCitizenUnderCursor)
                {
                    selectable = ResolveCitizen(hitEntity, hit.m_HitPosition);
                }

                // If no citizen is close enough to the cursor, fall back to JP's normal
                // building/useful-entity resolution. Destination selection always comes here.
                if (selectable == Entity.Null)
                {
                    selectable = _ui.WantsCitizenSelection
                        ? Entity.Null
                        : ResolveUsefulEntity(hitEntity, hit.m_HitPosition);
                }
            }

            // Highlight exactly what the next click will select. For citizens this means
            // the pedestrian receives the game's normal blue hover outline while JP is open.
            UpdateHighlight(selectable);

            if (cancelAction.WasPressedThisFrame())
            {
                _ui.CancelSelection();
                return inputDeps;
            }

            if (applyAction.WasPressedThisFrame())
            {
                if (!hasResult || selectable == Entity.Null || !EntityManager.Exists(selectable))
                {
                    _ui.PublishStatus("No usable map entity was found under the cursor.");
                    return inputDeps;
                }

                _ui.AcceptSelection(selectable, hit.m_HitPosition);
            }

            return inputDeps;
        }


        private Entity ResolveCitizen(Entity clicked, float3 hitPosition)
        {
            Entity direct = ResolveCitizenThroughOwners(clicked);
            if (direct != Entity.Null) return direct;
            return FindNearestCitizen(hitPosition, 10.0f);
        }

        private Entity ResolveCitizenThroughOwners(Entity clicked)
        {
            Entity current = clicked;
            for (int depth = 0; depth < 12; depth++)
            {
                if (current == Entity.Null || !EntityManager.Exists(current)) break;
                if (EntityManager.HasComponent<Human>(current) || EntityManager.HasComponent<Game.Creatures.Resident>(current)) return current;
                if (!EntityManager.HasComponent<Owner>(current)) break;
                Entity owner = EntityManager.GetComponentData<Owner>(current).m_Owner;
                if (owner == Entity.Null || owner == current) break;
                current = owner;
            }
            return Entity.Null;
        }

        private Entity FindNearestCitizen(float3 hitPosition, float maxDistance)
        {
            EntityQuery query = GetEntityQuery(ComponentType.ReadOnly<Game.Objects.Transform>());
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.TempJob))
            using (NativeArray<Game.Objects.Transform> transforms = query.ToComponentDataArray<Game.Objects.Transform>(Allocator.TempJob))
            {
                Entity best = Entity.Null;
                float bestDistanceSq = maxDistance * maxDistance;
                for (int i = 0; i < entities.Length; i++)
                {
                    float distanceSq = math.distancesq(transforms[i].m_Position, hitPosition);
                    if (distanceSq >= bestDistanceSq) continue;
                    Entity citizen = ResolveCitizenThroughOwners(entities[i]);
                    if (citizen == Entity.Null) continue;
                    bestDistanceSq = distanceSq;
                    best = citizen;
                }
                return best;
            }
        }

        private Entity ResolveUsefulEntity(Entity hit, float3 hitPosition)
        {
            if (hit == Entity.Null || !EntityManager.Exists(hit))
                return FindNearestBuilding(hitPosition, 35.0f);

            Entity current = hit;

            // CurrentLocation is reliable for buildings and for actual humans.
            // Never return a Route/Waypoint/Line Tool entity as A/B: those were
            // the cause of the v0.3.2 request hanging forever.
            for (int depth = 0; depth < 12; depth++)
            {
                if (current == Entity.Null || !EntityManager.Exists(current))
                    break;

                if (EntityManager.HasComponent<Building>(current))
                    return current;
                if (EntityManager.HasComponent<Human>(current) ||
                    EntityManager.HasComponent<Game.Creatures.Resident>(current))
                    return current;

                if (!EntityManager.HasComponent<Owner>(current))
                    break;

                Entity owner = EntityManager.GetComponentData<Owner>(current).m_Owner;
                if (owner == Entity.Null || owner == current)
                    break;
                current = owner;
            }

            // Clicking a stop, route line, lane, prop or attachment often hits an
            // entity that is not a valid CurrentLocation target. Resolve the click
            // spatially to the nearest real building instead of returning that entity.
            return FindNearestBuilding(hitPosition, 35.0f);
        }

        private Entity FindNearestBuilding(float3 hitPosition, float maxDistance)
        {
            EntityQuery query = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Game.Objects.Transform>());

            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.TempJob))
            using (NativeArray<Game.Objects.Transform> transforms =
                   query.ToComponentDataArray<Game.Objects.Transform>(Allocator.TempJob))
            {
                Entity best = Entity.Null;
                float bestDistanceSq = maxDistance * maxDistance;
                for (int i = 0; i < entities.Length; i++)
                {
                    float distanceSq = math.distancesq(transforms[i].m_Position, hitPosition);
                    if (distanceSq >= bestDistanceSq)
                        continue;
                    bestDistanceSq = distanceSq;
                    best = entities[i];
                }
                return best;
            }
        }

        private void UpdateHighlight(Entity entity)
        {
            if (entity == _highlighted)
                return;

            ClearHighlight();
            if (entity == Entity.Null || !EntityManager.Exists(entity))
                return;

            try
            {
                if (!EntityManager.HasComponent<Highlighted>(entity))
                    EntityManager.AddComponent<Highlighted>(entity);
                _highlighted = entity;
            }
            catch
            {
                _highlighted = Entity.Null;
            }
        }

        private void ClearHighlight()
        {
            if (_highlighted != Entity.Null && EntityManager.Exists(_highlighted))
            {
                try
                {
                    if (EntityManager.HasComponent<Highlighted>(_highlighted))
                        EntityManager.RemoveComponent<Highlighted>(_highlighted);
                }
                catch { }
            }
            _highlighted = Entity.Null;
        }

        public void ReturnToDefaultTool()
        {
            if (m_ToolSystem != null && m_DefaultToolSystem != null && m_ToolSystem.activeTool == this)
                m_ToolSystem.activeTool = m_DefaultToolSystem;
        }
    }
}
