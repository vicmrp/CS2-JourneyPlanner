using System;
using Colossal.Collections;
using Colossal.Mathematics;
using Game.Buildings;
using Game.Common;
using Game.Creatures;
using Game.Objects;
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
        private Game.Objects.SearchSystem _searchSystem;
        private Entity _lastHit = Entity.Null;
        private Entity _hoverEntity = Entity.Null;
        private float3 _lastHitPosition;
        private float _nextHoverTime;

        public override string toolID => "JourneyPlannerNativeTool";

        protected override void OnCreate()
        {
            base.OnCreate();
            _ui = World.GetOrCreateSystemManaged<JourneyPlannerUISystem>();
            _searchSystem = World.GetOrCreateSystemManaged<Game.Objects.SearchSystem>();
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            applyAction.shouldBeEnabled = true;
            cancelAction.shouldBeEnabled = true;
            InitializeRaycast();
            _nextHoverTime = 0f;
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
            bool clicked = applyAction.WasPressedThisFrame();

            Entity selectable = Entity.Null;
            bool refresh = clicked || hitEntity != _lastHit || UnityEngine.Time.realtimeSinceStartup >= _nextHoverTime ||
                           math.distancesq(hit.m_HitPosition, _lastHitPosition) > 1f;
            if (hasResult && !refresh && (_hoverEntity == Entity.Null || EntityManager.Exists(_hoverEntity)))
                selectable = _hoverEntity;
            else if (hasResult)
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
            _hoverEntity = selectable;
            if (refresh)
            {
                _lastHit = hitEntity;
                _lastHitPosition = hit.m_HitPosition;
                _nextHoverTime = UnityEngine.Time.realtimeSinceStartup + 0.1f;
            }

            // Highlight exactly what the next click will select. For citizens this means
            // the pedestrian receives the game's normal blue hover outline while JP is open.
            UpdateHighlight(selectable);

            if (cancelAction.WasPressedThisFrame())
            {
                _ui.Close();
                return inputDeps;
            }

            if (clicked)
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
            var tree = _searchSystem.GetMovingSearchTree(true, out JobHandle dependencies);
            dependencies.Complete();
            var iterator = CreateNearbyIterator(hitPosition, maxDistance, true);
            tree.Iterate(ref iterator);
            return iterator.Best;
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
            var tree = _searchSystem.GetStaticSearchTree(true, out JobHandle dependencies);
            dependencies.Complete();
            var iterator = CreateNearbyIterator(hitPosition, maxDistance, false);
            tree.Iterate(ref iterator);
            return iterator.Best;
        }

        private NearbyIterator CreateNearbyIterator(float3 position, float radius, bool citizens)
        {
            return new NearbyIterator
            {
                Position = position,
                Bounds = new Bounds2(position.xz - radius, position.xz + radius),
                BestDistanceSq = radius * radius,
                Citizens = citizens,
                Transforms = GetComponentLookup<Game.Objects.Transform>(true),
                Humans = GetComponentLookup<Human>(true),
                Residents = GetComponentLookup<Game.Creatures.Resident>(true),
                Buildings = GetComponentLookup<Building>(true),
                Owners = GetComponentLookup<Owner>(true),
                Deleted = GetComponentLookup<Deleted>(true)
            };
        }

        // Visit only objects in the small area under the cursor. The game's trees
        // are already maintained by the simulation; JP never copies a city query.
        private struct NearbyIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public float3 Position;
            public Bounds2 Bounds;
            public float BestDistanceSq;
            public bool Citizens;
            public Entity Best;
            public ComponentLookup<Game.Objects.Transform> Transforms;
            public ComponentLookup<Human> Humans;
            public ComponentLookup<Game.Creatures.Resident> Residents;
            public ComponentLookup<Building> Buildings;
            public ComponentLookup<Owner> Owners;
            public ComponentLookup<Deleted> Deleted;

            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, Bounds);

            public void Iterate(QuadTreeBoundsXZ bounds, Entity entity)
            {
                if (!Intersect(bounds) || Deleted.HasComponent(entity) || !Transforms.HasComponent(entity)) return;
                float distanceSq = math.distancesq(Transforms[entity].m_Position, Position);
                if (distanceSq >= BestDistanceSq) return;
                Entity current = entity;
                for (int depth = 0; depth < 12 && current != Entity.Null; depth++)
                {
                    if (Deleted.HasComponent(current)) return;
                    if (Citizens ? Humans.HasComponent(current) || Residents.HasComponent(current) : Buildings.HasComponent(current))
                    {
                        Best = current;
                        BestDistanceSq = distanceSq;
                        return;
                    }
                    if (!Owners.HasComponent(current)) return;
                    Entity owner = Owners[current].m_Owner;
                    if (owner == current) return;
                    current = owner;
                }
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
