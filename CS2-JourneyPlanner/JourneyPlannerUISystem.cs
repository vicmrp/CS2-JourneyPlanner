using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Colossal.UI.Binding;
using Colossal.Mathematics;
using Game.Prefabs;
using Game.Buildings;
using Game.Common;
using Game.Creatures;
using Game.Pathfind;
using Game.Net;
using Game.Objects;
using Game.Routes;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace CS2_JourneyPlanner
{
    public sealed partial class JourneyPlannerUISystem : UISystemBase
    {
        private const string Group = "JourneyPlannerNative";
        private const int MaxWaitFrames = 900;
        private const int ComparisonSampleInterval = 30;
        private const int NativeVisualSampleInterval = 30;
        private const int CurveSamples = 14;

        private JourneyPlannerToolSystem _tool;
        private ToolSystem _toolSystem;
        private NameSystem _nameSystem;
        private PathfindSetupSystem _pathfindSetupSystem;

        private SelectionMode _selectionMode;
        private bool _waitingForVanillaInfoClose;
        private bool _isOpen;
        private bool _awaitingDestination;
        private Entity _origin = Entity.Null;
        private Entity _destination = Entity.Null;
        private Entity _probe = Entity.Null;
        private int _requestFrame;
        private bool _waitingForPath;

        private Entity _comparisonCitizen = Entity.Null;
        private bool _comparisonEnabled;
        private int _comparisonFrame;
        private string _comparisonFolder;
        private string _lastCitizenPathFingerprint = String.Empty;
        private string _lastPlannerResult = String.Empty;

        private bool _nativeVisualCaptureEnabled;
        private int _nativeVisualFrame;
        private readonly Dictionary<string, string> _livePathFingerprints = new Dictionary<string, string>();
        private int _nativeVisualChangeNumber;

        private bool _routeVisible = true;
        private JourneyPlannerRouteSystem _routeRenderer;
        private List<JourneyLeg> _journeyLegs;
        private float _nextColorRefresh;
        private int _followPointIndex;
        private float3 _lastFollowPosition;
        private bool _hasFollowPosition;

        // Citizen-origin mode: A is the citizen, B is resolved from Game.Common.Target.
        // The rendered route is progressively consumed behind the citizen/vehicle.
        private Entity _followCitizen = Entity.Null;
        private readonly List<RoutePiece> _routePieces = new List<RoutePiece>();
        private int _followPieceIndex;
        private int _followFrame;
        private const int FollowUpdateInterval = 6;
        private const float FollowAcquireDistance = 55f;

        // v0.5.1: player-facing A→B planning is intentionally public-transport-only.
        // Citizen-origin mode still uses the citizen's own live native PathElement
        // buffer as ground truth, including any private-vehicle legs CS2 chose.
        private string _activeRequestMode = "PublicTransport";
        private bool _citizenOriginMode;

        private ValueBinding<bool> _visibleBinding;
        private ValueBinding<string> _statusBinding;
        private ValueBinding<string> _originBinding;
        private ValueBinding<string> _destinationBinding;
        private ValueBinding<string> _resultBinding;
        private ValueBinding<bool> _busyBinding;
        private ValueBinding<bool> _comparisonEnabledBinding;
        private ValueBinding<string> _comparisonCitizenBinding;
        private ValueBinding<string> _comparisonFolderBinding;
        private ValueBinding<bool> _routeVisibleBinding;
        private ValueBinding<bool> _nativeVisualCaptureEnabledBinding;
        private ValueBinding<string> _nativeVisualStatusBinding;
        private ValueBinding<bool> _citizenOriginBinding;
        private ValueBinding<bool> _awaitingDestinationBinding;
        private ValueBinding<string> _journeyJsonBinding;

        protected override void OnCreate()
        {
            base.OnCreate();
            _tool = World.GetOrCreateSystemManaged<JourneyPlannerToolSystem>();
            _routeRenderer = World.GetOrCreateSystemManaged<JourneyPlannerRouteSystem>();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _nameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            _pathfindSetupSystem = World.GetOrCreateSystemManaged<PathfindSetupSystem>();

            _visibleBinding = new ValueBinding<bool>(Group, "Visible", false);
            _statusBinding = new ValueBinding<string>(Group, "Status", "Choose origin A and destination B.");
            _originBinding = new ValueBinding<string>(Group, "Origin", "Not selected");
            _destinationBinding = new ValueBinding<string>(Group, "Destination", "Not selected");
            _resultBinding = new ValueBinding<string>(Group, "Result", "No journey calculated yet.");
            _busyBinding = new ValueBinding<bool>(Group, "Busy", false);
            _comparisonEnabledBinding = new ValueBinding<bool>(Group, "ComparisonEnabled", false);
            _comparisonCitizenBinding = new ValueBinding<string>(Group, "ComparisonCitizen", "No citizen selected");
            _comparisonFolderBinding = new ValueBinding<string>(Group, "ComparisonFolder", "");
            _routeVisibleBinding = new ValueBinding<bool>(Group, "RouteVisible", true);
            _nativeVisualCaptureEnabledBinding = new ValueBinding<bool>(Group, "NativeVisualCaptureEnabled", false);
            _nativeVisualStatusBinding = new ValueBinding<string>(Group, "NativeVisualStatus", "Native LivePath capture is off.");
            _citizenOriginBinding = new ValueBinding<bool>(Group, "CitizenOrigin", false);
            _awaitingDestinationBinding = new ValueBinding<bool>(Group, "AwaitingDestination", false);
            _journeyJsonBinding = new ValueBinding<string>(Group, "JourneyJson", "{\"ready\":false}");

            AddBinding(_visibleBinding);
            AddBinding(_statusBinding);
            AddBinding(_originBinding);
            AddBinding(_destinationBinding);
            AddBinding(_resultBinding);
            AddBinding(_busyBinding);
            AddBinding(_comparisonEnabledBinding);
            AddBinding(_comparisonCitizenBinding);
            AddBinding(_comparisonFolderBinding);
            AddBinding(_routeVisibleBinding);
            AddBinding(_nativeVisualCaptureEnabledBinding);
            AddBinding(_nativeVisualStatusBinding);
            AddBinding(_citizenOriginBinding);
            AddBinding(_awaitingDestinationBinding);
            AddBinding(_journeyJsonBinding);

            AddBinding(new TriggerBinding(Group, "Open", Open));
            AddBinding(new TriggerBinding(Group, "Close", Close));
            AddBinding(new TriggerBinding(Group, "SelectOrigin", SelectOrigin));
            AddBinding(new TriggerBinding(Group, "SelectDestination", SelectDestination));
            AddBinding(new TriggerBinding(Group, "Calculate", Calculate));
            AddBinding(new TriggerBinding(Group, "Clear", Clear));
            AddBinding(new TriggerBinding(Group, "SelectComparisonCitizen", SelectComparisonCitizen));
            AddBinding(new TriggerBinding(Group, "ToggleComparison", ToggleComparison));
            AddBinding(new TriggerBinding(Group, "ToggleRoute", ToggleRoute));
            AddBinding(new TriggerBinding(Group, "DeleteRoute", DeleteRoute));
            AddBinding(new TriggerBinding(Group, "ToggleNativeVisualCapture", ToggleNativeVisualCapture));
            AddBinding(new TriggerBinding(Group, "SnapshotNativeVisual", SnapshotNativeVisual));
            AddBinding(new TriggerBinding(Group, "RefollowCitizen", RefollowCitizen));
            AddBinding(new TriggerBinding(Group, "OpenOriginInfo", OpenOriginInfo));
            AddBinding(new TriggerBinding(Group, "OpenDestinationInfo", OpenDestinationInfo));
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();

            // Explicit Start/Destination row clicks temporarily hand control to the
            // vanilla DefaultToolSystem so its normal entity info panel can stay open.
            // Do not take control back on a timer: that immediately closes the panel.
            // Instead wait until vanilla clears ToolSystem.selected (panel closed),
            // then return to JP's world-selection mode.
            if (_waitingForVanillaInfoClose && _isOpen)
            {
                try
                {
                    Entity vanillaSelected = _toolSystem.selected;
                    if (vanillaSelected == Entity.Null)
                    {
                        _waitingForVanillaInfoClose = false;
                        ResumeAutoSelection();
                        _statusBinding.Update("JP selection resumed.");
                    }
                }
                catch
                {
                    // If a future game build changes this property, do not spam logs
                    // or continually change tools. The user can close/reopen JP.
                }
            }

            UpdateCitizenRouteProgress();
            RefreshRouteColors();

            if (!_waitingForPath)
                return;

            _requestFrame++;
            if (_probe == Entity.Null || !EntityManager.Exists(_probe))
            {
                FinishFailure("Temporary path probe disappeared.");
                return;
            }

            if (EntityManager.HasBuffer<PathElement>(_probe))
            {
                DynamicBuffer<PathElement> path = EntityManager.GetBuffer<PathElement>(_probe, true);
                if (path.Length > 0)
                {
                    List<JourneyLeg> legs = PrepareJourney(path);
                    string result = ParseJourney(path, legs);
                    _lastPlannerResult = result;
                    _resultBinding.Update(result);
                    _journeyJsonBinding.Update(BuildJourneyJson(legs));
                    RenderRoute(path, legs);
                    _statusBinding.Update("Journey ready.");
                    _waitingForPath = false;
                    _busyBinding.Update(false);
                    DestroyProbe();
                    return;
                }
            }

            if (_requestFrame >= MaxWaitFrames)
                FinishFailure("No populated PathElement buffer arrived. Unpause the simulation and try again.");
        }

        private void Open()
        {
            _isOpen = true;
            _visibleBinding.Update(true);
            ClearVanillaSelection();
            _selectionMode = SelectionMode.Auto;
            _toolSystem.activeTool = _tool;

            if (_origin == Entity.Null)
                _statusBinding.Update("JP ready. Click a citizen for their current journey, or click a building to choose start A.");
            else if (_awaitingDestination)
                _statusBinding.Update("Start A is selected. Click a destination building.");
        }

        public void Close()
        {
            _isOpen = false;
            _waitingForVanillaInfoClose = false;
            _selectionMode = SelectionMode.None;
            _tool?.ReturnToDefaultTool();
            _visibleBinding.Update(false);
        }

        private Entity GetVanillaSelectedEntity()
        {
            try
            {
                PropertyInfo selectedProp = _toolSystem.GetType().GetProperty(
                    "selected", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (selectedProp != null && selectedProp.CanRead)
                {
                    object raw = selectedProp.GetValue(_toolSystem, null);
                    if (raw is Entity entity)
                        return entity;
                }
            }
            catch (Exception ex)
            {
                Mod.Log.Warn("Could not read ToolSystem.selected: " + ex.GetBaseException().Message);
            }
            return Entity.Null;
        }

        private Entity ResolveSelectedLocation(Entity selected)
        {
            Entity current = selected;
            for (int depth = 0; depth < 12; depth++)
            {
                if (current == Entity.Null || !EntityManager.Exists(current))
                    return Entity.Null;

                if (EntityManager.HasComponent<Game.Buildings.Building>(current))
                    return current;

                if (!EntityManager.HasComponent<Owner>(current))
                    break;

                Entity owner = EntityManager.GetComponentData<Owner>(current).m_Owner;
                if (owner == Entity.Null || owner == current)
                    break;
                current = owner;
            }
            return Entity.Null;
        }

        private void UseSelectedCitizen()
        {
            Open();
            CancelSelection();

            Entity selected = GetVanillaSelectedEntity();
            Entity citizen = ResolveCitizenFromEntity(selected);
            if (!IsCitizenEntity(citizen))
            {
                _statusBinding.Update(selected == Entity.Null
                    ? "No entity is currently selected in the normal CS2 UI. Select a citizen first, then press Use selected citizen."
                    : "The entity selected in the normal CS2 UI is not a citizen. Select a citizen first, then press Use selected citizen.");
                return;
            }

            _origin = citizen;
            _originBinding.Update(DescribeEntity(citizen) + " | selected citizen");
            _citizenOriginMode = true;
            _citizenOriginBinding.Update(true);
            _followCitizen = citizen;
            _comparisonCitizen = citizen;
            _comparisonCitizenBinding.Update(DescribeEntity(citizen));
            _lastCitizenPathFingerprint = String.Empty;

            Entity automaticDestination = ResolveCitizenDestination(citizen);
            if (automaticDestination != Entity.Null)
            {
                _destination = automaticDestination;
                _destinationBinding.Update(DescribeEntity(automaticDestination) + " | automatic citizen destination");
                _statusBinding.Update("Selected CS2 citizen loaded into A. Destination B was resolved from the citizen's current Target. Press Calculate native journey.");
            }
            else
            {
                _destination = Entity.Null;
                _destinationBinding.Update("Could not resolve citizen destination");
                _statusBinding.Update("Selected CS2 citizen loaded into A, but its current destination could not be resolved. Choose destination B manually.");
            }
        }

        private void SelectOrigin()
        {
            Open();
            _selectionMode = SelectionMode.Origin;
            ClearVanillaSelection();
            _statusBinding.Update("Choose start A: click a citizen or building.");
            _toolSystem.activeTool = _tool;
        }

        private void SelectDestination()
        {
            Open();
            _selectionMode = SelectionMode.Destination;
            ClearVanillaSelection();
            _statusBinding.Update("Choose destination B: click a building.");
            _toolSystem.activeTool = _tool;
        }

        private void SelectComparisonCitizen()
        {
            Open();
            _selectionMode = SelectionMode.ComparisonCitizen;
            _statusBinding.Update("Select the citizen whose real journey should be compared with the planner.");
            _toolSystem.activeTool = _tool;
        }

        public bool WantsCitizenSelection => _selectionMode == SelectionMode.ComparisonCitizen;

        // While choosing start A, prefer a citizen under/near the cursor even if
        // the raycast first hits a stop, platform, lane, shelter, or other surface.
        // Destination B remains building-only.
        public bool PreferCitizenUnderCursor =>
            _selectionMode == SelectionMode.ComparisonCitizen ||
            _selectionMode == SelectionMode.Origin ||
            (_selectionMode == SelectionMode.Auto && !_awaitingDestination);

        public void CancelSelection()
        {
            _selectionMode = SelectionMode.None;
            _tool?.ReturnToDefaultTool();
        }

        public void PublishStatus(string message) => _statusBinding.Update(message ?? "Selection failed.");

        public void AcceptSelection(Entity entity, float3 position)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
            {
                PublishStatus("No usable entity was selected.");
                return;
            }

            // JP owns world selection while its panel is open. Keep vanilla selection
            // empty so CS2 does not open the normal info panel for every map click.
            ClearVanillaSelection();

            if (_selectionMode == SelectionMode.ComparisonCitizen)
            {
                Entity comparison = ResolveCitizenFromEntity(entity);
                if (!IsCitizenEntity(comparison))
                {
                    PublishStatus("Select a citizen for comparison.");
                    return;
                }
                _comparisonCitizen = comparison;
                _comparisonCitizenBinding.Update(DescribeEntity(comparison));
                _lastCitizenPathFingerprint = String.Empty;
                _statusBinding.Update("Comparison citizen selected.");
                if (_comparisonEnabled) EnsureComparisonSession();
                CancelSelection();
                return;
            }

            // Citizens always win in automatic/origin mode. Selecting one immediately
            // displays the citizen's own current native route when one exists.
            Entity citizen = ResolveCitizenFromEntity(entity);
            if ((_selectionMode == SelectionMode.Auto || _selectionMode == SelectionMode.Origin) &&
                IsCitizenEntity(citizen))
            {
                SelectCitizenJourney(citizen);
                ResumeAutoSelection();
                return;
            }

            Entity building = ResolveBuildingFromEntity(entity);
            if (!IsBuildingEntity(building))
            {
                PublishStatus(_selectionMode == SelectionMode.Destination
                    ? "Destination must be a building."
                    : "Click a citizen or building.");
                return;
            }

            if (_selectionMode == SelectionMode.Destination ||
                (_selectionMode == SelectionMode.Auto && _awaitingDestination))
            {
                SelectBuildingDestination(building);
                ResumeAutoSelection();
                return;
            }

            // Explicit Origin or an ordinary building click in Auto mode starts a new
            // building-to-building journey and waits for the next building click.
            SelectBuildingOrigin(building);
            ResumeAutoSelection();
        }

        private void SelectCitizenJourney(Entity citizen)
        {
            DestroyProbe();
            DestroyRouteOverlay();

            _origin = citizen;
            _originBinding.Update(DisplayName(citizen));
            _citizenOriginMode = true;
            _citizenOriginBinding.Update(true);
            _followCitizen = citizen;
            _comparisonCitizen = citizen;
            _comparisonCitizenBinding.Update(DescribeEntity(citizen));
            _lastCitizenPathFingerprint = String.Empty;
            _awaitingDestination = false;
            _awaitingDestinationBinding.Update(false);

            Entity automaticDestination = ResolveCitizenDestination(citizen);
            _destination = automaticDestination;
            _destinationBinding.Update(automaticDestination != Entity.Null
                ? DisplayName(automaticDestination)
                : "Current destination not resolved");

            _journeyJsonBinding.Update("{\"ready\":false}");
            _statusBinding.Update("Citizen selected. Reading CS2's current native journey…");
            Calculate();
        }

        private void SelectBuildingOrigin(Entity building)
        {
            DestroyProbe();
            DestroyRouteOverlay();

            _origin = building;
            _destination = Entity.Null;
            _originBinding.Update(DisplayName(building));
            _destinationBinding.Update("Click a destination building…");
            _citizenOriginMode = false;
            _citizenOriginBinding.Update(false);
            _followCitizen = Entity.Null;
            _awaitingDestination = true;
            _awaitingDestinationBinding.Update(true);
            _journeyJsonBinding.Update("{\"ready\":false}");
            _statusBinding.Update("Start A selected. Now click a destination building.");
        }

        private void SelectBuildingDestination(Entity building)
        {
            if (_origin == building)
            {
                _statusBinding.Update("Choose a destination different from the start building.");
                return;
            }

            _destination = building;
            _destinationBinding.Update(DisplayName(building));
            _awaitingDestination = false;
            _awaitingDestinationBinding.Update(false);
            _journeyJsonBinding.Update("{\"ready\":false}");
            _statusBinding.Update("Destination B selected. Calculating…");
            Calculate();
        }

        private void ResumeAutoSelection()
        {
            if (!_isOpen)
                return;
            _selectionMode = SelectionMode.Auto;
            if (_toolSystem.activeTool != _tool)
                _toolSystem.activeTool = _tool;
        }

        private Entity ResolveBuildingFromEntity(Entity entity)
        {
            Entity current = entity;
            for (int depth = 0; depth < 12; depth++)
            {
                if (current == Entity.Null || !EntityManager.Exists(current))
                    return Entity.Null;
                if (EntityManager.HasComponent<Building>(current))
                    return current;
                if (!EntityManager.HasComponent<Owner>(current))
                    return Entity.Null;
                Entity owner = EntityManager.GetComponentData<Owner>(current).m_Owner;
                if (owner == Entity.Null || owner == current)
                    return Entity.Null;
                current = owner;
            }
            return Entity.Null;
        }

        private bool IsBuildingEntity(Entity entity)
        {
            return entity != Entity.Null && EntityManager.Exists(entity) &&
                   EntityManager.HasComponent<Building>(entity);
        }

        private void ClearVanillaSelection()
        {
            try
            {
                PropertyInfo selectedProp = _toolSystem.GetType().GetProperty(
                    "selected", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (selectedProp != null && selectedProp.CanWrite)
                    selectedProp.SetValue(_toolSystem, Entity.Null, null);
            }
            catch (Exception ex)
            {
                Mod.Log.Warn("Could not clear ToolSystem.selected: " + ex.GetBaseException().Message);
            }
        }

        private void OpenOriginInfo() => ShowVanillaInfo(_origin, "start");
        private void OpenDestinationInfo() => ShowVanillaInfo(_destination, "destination");

        private void ShowVanillaInfo(Entity entity, string role)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
            {
                _statusBinding.Update("No " + role + " entity is selected yet.");
                return;
            }

            try
            {
                // Vanilla entity panels are driven while DefaultToolSystem owns the
                // normal selection state. JP normally keeps its own tool active so
                // ordinary map clicks do NOT open those panels. For an explicit row
                // click only, temporarily hand control back to vanilla, select the
                // saved entity, then restore JP after several UI frames.
                _tool?.ReturnToDefaultTool();

                PropertyInfo selectedProp = _toolSystem.GetType().GetProperty(
                    "selected", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (selectedProp != null && selectedProp.CanWrite)
                {
                    selectedProp.SetValue(_toolSystem, entity, null);
                    _waitingForVanillaInfoClose = true;
                    _statusBinding.Update(
                        "Opened the normal CS2 info panel for " + role +
                        ". JP selection resumes when that panel is closed.");
                }
                else
                {
                    ResumeAutoSelection();
                    _statusBinding.Update("Could not open the normal CS2 info panel on this game build.");
                }
            }
            catch (Exception ex)
            {
                ResumeAutoSelection();
                _statusBinding.Update("Could not open CS2 info panel: " + ex.GetBaseException().Message);
            }
        }


        private void RefollowCitizen()
        {
            Entity citizen = _followCitizen != Entity.Null ? _followCitizen : _comparisonCitizen;
            if (!IsCitizenEntity(citizen))
            {
                _statusBinding.Update("No citizen is attached to this journey.");
                return;
            }
            // Use the same public focus entry point as the game's selected-entity
            // panel. Keep its selection handoff active until that panel is closed.
            ShowVanillaInfo(citizen, "citizen");
            World.GetOrCreateSystemManaged<Game.UI.InGame.SelectedInfoUISystem>().Focus(citizen);
            _statusBinding.Update("Following the selected citizen with the native CS2 camera.");
        }

        private bool TryUseCitizenNativeJourney()
        {
            if (!_citizenOriginMode || !IsCitizenEntity(_origin))
                return false;

            // Refresh B because a citizen's Target can change while the UI is open.
            Entity destination = ResolveCitizenDestination(_origin);
            if (destination != Entity.Null)
            {
                _destination = destination;
                _destinationBinding.Update(DescribeEntity(destination) + " | automatic citizen destination");
            }

            if (!EntityManager.HasBuffer<PathElement>(_origin))
            {
                _statusBinding.Update("Citizen currently has no native PathElement buffer. Unpause briefly and try Calculate again.");
                return true;
            }

            DynamicBuffer<PathElement> path = EntityManager.GetBuffer<PathElement>(_origin, true);
            if (path.Length == 0)
            {
                _statusBinding.Update("Citizen native path is currently empty. Unpause briefly and try Calculate again.");
                return true;
            }

            _activeRequestMode = "CitizenActual";
            List<JourneyLeg> legs = PrepareJourney(path);
            string result = ParseJourney(path, legs);
            _lastPlannerResult = result;
            _resultBinding.Update(result);
            _journeyJsonBinding.Update(BuildJourneyJson(legs));
            RenderRoute(path, legs);
            _busyBinding.Update(false);
            _waitingForPath = false;
            _statusBinding.Update("Using the selected citizen's own native path (" + path.Length +
                                  " PathElements). This is the route CS2 actually chose.");
            return true;
        }

        private static PathMethod GetRequestedMethods()
        {
            // v0.5.1 deliberately supports only the reliable public-transport
            // planner. Walking is included because access, transfer and egress
            // legs are part of a public-transport journey.
            return PathMethod.Pedestrian |
                   PathMethod.PublicTransportDay |
                   PathMethod.PublicTransportNight;
        }

        private void Calculate()
        {
            if (_origin == Entity.Null || !EntityManager.Exists(_origin))
            {
                _statusBinding.Update("Select origin A first.");
                return;
            }
            // Citizen mode can render the citizen's native PathElement buffer even
            // when its Target could not be resolved to a building.
            if (TryUseCitizenNativeJourney())
                return;

            if (_destination == Entity.Null || !EntityManager.Exists(_destination))
            {
                _statusBinding.Update("Select destination B first.");
                return;
            }

            DestroyProbe();

            try
            {
                _probe = EntityManager.CreateEntity(typeof(PathOwner));
                EntityManager.AddBuffer<PathElement>(_probe);

                PathOwner pathOwner = default;
                HumanCurrentLane currentLane = default;

                PathMethod transitMethods = GetRequestedMethods();
                _activeRequestMode = "PublicTransport";

                PathfindParameters parameters = default;
                parameters.m_MaxSpeed = new float2(277.7778f, 277.7778f);
                parameters.m_WalkSpeed = new float2(1.666667f, 1.666667f);
                parameters.m_Weights = new PathfindWeights(1f, 1f, 1f, 1f);
                parameters.m_Methods = transitMethods;
                parameters.m_MaxCost = 1000000f;

                SetupQueueTarget origin = default;
                origin.m_Type = SetupTargetType.CurrentLocation;
                origin.m_Methods = transitMethods;
                origin.m_Entity = _origin;
                origin.m_RandomCost = 0f;

                SetupQueueTarget destination = default;
                destination.m_Type = SetupTargetType.CurrentLocation;
                destination.m_Methods = transitMethods;
                destination.m_Entity = _destination;
                destination.m_RandomCost = 0f;

                NativeQueue<SetupQueueItem> queue = _pathfindSetupSystem.GetQueue(this, 1, 1);
                SetupQueueItem item = new SetupQueueItem(_probe, parameters, origin, destination);
                CreatureUtils.SetupPathfind(ref currentLane, ref pathOwner, queue.AsParallelWriter(), item);
                EntityManager.SetComponentData(_probe, pathOwner);

                _requestFrame = 0;
                _waitingForPath = true;
                _busyBinding.Update(true);
                _resultBinding.Update("Waiting for CS2 native pathfinder…");
                _journeyJsonBinding.Update("{\"ready\":false,\"busy\":true}");
                _statusBinding.Update("Native public-transport request submitted. Unpause the simulation for a few seconds.");

                Mod.Log.Info("Native journey request submitted. Probe=" + _probe +
                             " Origin=" + _origin + " Destination=" + _destination +
                             " Methods=" + transitMethods);
            }
            catch (Exception ex)
            {
                FinishFailure("Path request failed: " + ex.GetBaseException().Message);
                Mod.Log.Error("Native journey request failed: " + ex);
            }
        }

        private string DisplayName(Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
                return "Unknown";
            string name = ResolveName(entity);
            if (String.IsNullOrWhiteSpace(name) || name.StartsWith("Assets.NAME[", StringComparison.Ordinal))
                return name ?? "Unknown";
            return name;
        }

        private string TransitStopName(Entity waypoint)
        {
            if (waypoint != Entity.Null && EntityManager.Exists(waypoint) && EntityManager.HasComponent<Connected>(waypoint))
            {
                Entity connected = EntityManager.GetComponentData<Connected>(waypoint).m_Connected;
                if (connected != Entity.Null && EntityManager.Exists(connected))
                    return DisplayName(connected);
            }
            return DisplayName(waypoint);
        }

        private float CalculateLegDistance(DynamicBuffer<PathElement> path, JourneyLeg leg)
        {
            float total = 0f;
            for (int i = leg.StartPathIndex; i <= leg.EndPathIndex && i < path.Length; i++)
            {
                if (!TryGetBezier(path[i].m_Target, out Bezier4x3 source) ||
                    !JourneyGeometry.TryTrim(source, path[i].m_TargetDelta, out Bezier4x3 curve))
                    continue;
                total += JourneyGeometry.Length(curve, CurveSamples);
            }
            return total;
        }

        private static int EstimateWalkMinutes(float meters)
        {
            if (meters <= 0.1f) return 0;
            // 1.4 m/s: UI estimate only. Geometry comes from the native path.
            return Math.Max(1, (int)Math.Round((meters / 1.4f) / 60f));
        }

        private static string JsonEscape(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            return value.Replace("\\", "\\\\")
                        .Replace("\"", "\\\"")
                        .Replace("\r", "\\r")
                        .Replace("\n", "\\n")
                        .Replace("\t", "\\t");
        }

        private string BuildJourneyJson(List<JourneyLeg> legs)
        {
            StringBuilder b = new StringBuilder();
            b.Append("{\"ready\":true");
            b.Append(",\"citizen\":").Append(_citizenOriginMode ? "true" : "false");
            b.Append(",\"origin\":\"").Append(JsonEscape(DisplayName(_origin))).Append("\"");
            b.Append(",\"destination\":\"").Append(JsonEscape(DisplayName(_destination))).Append("\"");
            b.Append(",\"legs\":[");

            for (int i = 0; i < legs.Count; i++)
            {
                if (i > 0) b.Append(',');
                JourneyLeg leg = legs[i];
                float distance = leg.DistanceMeters;
                int walkMinutes = leg.Mode == "Walk" ? EstimateWalkMinutes(distance) : 0;
                int stops = (!IsPrivateMode(leg.Mode) && leg.Mode != "Walk")
                    ? EstimateStopCount(leg.RouteOwner, leg.FirstWaypoint, leg.LastWaypoint)
                    : -1;

                string from = leg.Mode == "Walk" || IsPrivateMode(leg.Mode)
                    ? DisplayName(leg.FirstTarget)
                    : TransitStopName(leg.FirstTarget);
                string to = leg.Mode == "Walk" || IsPrivateMode(leg.Mode)
                    ? DisplayName(leg.LastTarget)
                    : TransitStopName(leg.LastTarget);

                b.Append('{');
                b.Append("\"mode\":\"").Append(JsonEscape(leg.Mode)).Append("\"");
                b.Append(",\"routeNumber\":").Append(leg.RouteNumber);
                b.Append(",\"color\":\"").Append(leg.ColorHex).Append("\"");
                b.Append(",\"routeName\":\"").Append(JsonEscape(leg.RouteName)).Append("\"");
                b.Append(",\"from\":\"").Append(JsonEscape(from)).Append("\"");
                b.Append(",\"to\":\"").Append(JsonEscape(to)).Append("\"");
                b.Append(",\"distanceMeters\":").Append(distance.ToString("0.0", CultureInfo.InvariantCulture));
                b.Append(",\"walkMinutes\":").Append(walkMinutes);
                b.Append(",\"stops\":").Append(stops);
                b.Append('}');
            }

            b.Append("]}");
            return b.ToString();
        }

        private string ParseJourney(DynamicBuffer<PathElement> path, List<JourneyLeg> legs)
        {

            // Suppress tiny connector-only walk runs at the very beginning/end only
            // if they contain zero elements (normally impossible). Keep all real runs
            // because transfer walks are important evidence.
            var b = new StringBuilder();
            b.AppendLine("NATIVE JOURNEY");
            b.AppendLine("==============");
            b.AppendLine("A: " + DescribeEntity(_origin));
            b.AppendLine("B: " + DescribeEntity(_destination));
            b.AppendLine("Source: " + (_citizenOriginMode ? "CITIZEN ACTUAL NATIVE PATH" : "PLANNER " + _activeRequestMode.ToUpperInvariant()));
            b.AppendLine("PathElements: " + path.Length);
            b.AppendLine("Legs: " + legs.Count);
            b.AppendLine();

            int display = 1;
            foreach (JourneyLeg leg in legs)
            {
                if (leg.Mode == "Walk" || IsPrivateMode(leg.Mode))
                {
                    int count = leg.EndPathIndex - leg.StartPathIndex + 1;
                    b.AppendLine(display + ". " + leg.Mode.ToUpperInvariant());
                    b.AppendLine("   PathElements " + leg.StartPathIndex + "–" + leg.EndPathIndex + " (" + count + ")");
                    b.AppendLine("   " + ResolveName(leg.FirstTarget) + " → " + ResolveName(leg.LastTarget));
                }
                else
                {
                    string number = leg.RouteNumber >= 0 ? " " + leg.RouteNumber : "";
                    b.AppendLine(display + ". " + leg.Mode.ToUpperInvariant() + number);
                    b.AppendLine("   Route owner: " + FormatEntity(leg.RouteOwner));
                    b.AppendLine("   Board: " + DescribeTransitPoint(leg.FirstTarget, leg.FirstWaypoint));
                    b.AppendLine("   Exit:  " + DescribeTransitPoint(leg.LastTarget, leg.LastWaypoint));

                    int stops = EstimateStopCount(leg.RouteOwner, leg.FirstWaypoint, leg.LastWaypoint);
                    if (stops >= 0)
                        b.AppendLine("   Route-waypoint hops: " + stops);
                    b.AppendLine("   PathElements " + leg.StartPathIndex + "–" + leg.EndPathIndex);
                }
                b.AppendLine();
                display++;
            }

            b.AppendLine("RAW TRANSIT ELEMENTS");
            b.AppendLine("--------------------");
            bool anyTransit = false;
            for (int i = 0; i < path.Length; i++)
            {
                TransitInfo info = GetTransitInfo(path[i].m_Target);
                if (!info.IsTransit)
                    continue;
                anyTransit = true;
                b.AppendLine("[" + i + "] " + DescribeEntity(path[i].m_Target) +
                             " | " + info.Mode +
                             " | route=" + FormatEntity(info.RouteOwner) +
                             " | number=" + (info.RouteNumber >= 0 ? info.RouteNumber.ToString(CultureInfo.InvariantCulture) : "?") +
                             " | waypoint=" + (info.WaypointIndex >= 0 ? info.WaypointIndex.ToString(CultureInfo.InvariantCulture) : "?"));
            }
            if (!anyTransit)
                b.AppendLine("No transit route elements were found. CS2 chose a walking-only route for this A→B request.");

            return b.ToString().TrimEnd();
        }

        private TransitInfo GetTransitInfo(Entity target)
        {
            var info = new TransitInfo
            {
                IsTransit = false,
                Mode = "",
                RouteOwner = Entity.Null,
                RouteNumber = -1,
                WaypointIndex = -1
            };

            if (target == Entity.Null || !EntityManager.Exists(target))
                return info;

            // Names are player-editable and localized. Use the route prefab's
            // transport type so renamed lines retain their identity and colour.
            bool isWaypoint = EntityManager.HasComponent<Waypoint>(target);
            bool isSegment = EntityManager.HasComponent<Game.Routes.Segment>(target);
            if ((!isWaypoint && !isSegment) || !EntityManager.HasComponent<Owner>(target)) return info;
            Entity route = EntityManager.GetComponentData<Owner>(target).m_Owner;
            if (route == Entity.Null || !EntityManager.Exists(route) || !EntityManager.HasComponent<PrefabRef>(route)) return info;
            Entity prefab = EntityManager.GetComponentData<PrefabRef>(route).m_Prefab;
            if (!EntityManager.Exists(prefab) || !EntityManager.HasComponent<TransportLineData>(prefab)) return info;
            switch (EntityManager.GetComponentData<TransportLineData>(prefab).m_TransportType)
            {
                case TransportType.Bus: info.Mode = "Bus"; break;
                case TransportType.Tram: info.Mode = "Tram"; break;
                case TransportType.Train: info.Mode = "Train"; break;
                case TransportType.Subway: info.Mode = "Metro"; break;
                case TransportType.Ferry:
                case TransportType.Ship: info.Mode = "Ship"; break;
                case TransportType.Airplane: info.Mode = "Air"; break;
                default: return info;
            }
            info.IsTransit = true;
            info.RouteOwner = route;
            if (isWaypoint)
            {
                info.WaypointIndex = EntityManager.GetComponentData<Waypoint>(target).m_Index;
                info.ExitWaypointIndex = info.WaypointIndex;
            }
            else
            {
                info.WaypointIndex = EntityManager.GetComponentData<Game.Routes.Segment>(target).m_Index;
                int count = EntityManager.HasBuffer<RouteWaypoint>(route) ? EntityManager.GetBuffer<RouteWaypoint>(route, true).Length : 0;
                info.ExitWaypointIndex = count > 0 ? (info.WaypointIndex + 1) % count : -1;
            }
            if (info.RouteOwner != Entity.Null && EntityManager.Exists(info.RouteOwner) && EntityManager.HasComponent<RouteNumber>(info.RouteOwner))
                info.RouteNumber = EntityManager.GetComponentData<RouteNumber>(info.RouteOwner).m_Number;

            return info;
        }

        private int EstimateStopCount(Entity route, int fromWaypoint, int toWaypoint)
        {
            if (route == Entity.Null || !EntityManager.Exists(route) || fromWaypoint < 0 || toWaypoint < 0)
                return -1;
            if (!EntityManager.HasBuffer<RouteWaypoint>(route))
                return -1;

            int count = EntityManager.GetBuffer<RouteWaypoint>(route, true).Length;
            if (count <= 0)
                return -1;
            return (toWaypoint - fromWaypoint + count) % count;
        }

        private string DescribeTransitPoint(Entity waypoint, int index)
        {
            string text = ResolveName(waypoint);
            if (waypoint != Entity.Null && EntityManager.Exists(waypoint) && EntityManager.HasComponent<Connected>(waypoint))
            {
                Entity connected = EntityManager.GetComponentData<Connected>(waypoint).m_Connected;
                if (connected != Entity.Null && EntityManager.Exists(connected))
                    text += " / " + ResolveName(connected);
            }
            if (index >= 0)
                text += " (waypoint " + index + ")";
            return text;
        }

        private void ToggleComparison()
        {
            _comparisonEnabled = !_comparisonEnabled;
            _comparisonEnabledBinding.Update(_comparisonEnabled);

            if (_comparisonEnabled)
            {
                EnsureComparisonSession();
                _statusBinding.Update(_comparisonCitizen == Entity.Null
                    ? "Comparison logging enabled. Select a comparison citizen."
                    : "Comparison logging enabled. Run the simulation to capture the citizen journey.");
                if (!String.IsNullOrWhiteSpace(_lastPlannerResult))
                    WriteTextSafe(Path.Combine(_comparisonFolder, "planner-proposed.txt"), _lastPlannerResult + Environment.NewLine);
            }
            else
            {
                _statusBinding.Update("Comparison logging stopped.");
            }
        }

        private void ToggleNativeVisualCapture()
        {
            _nativeVisualCaptureEnabled = !_nativeVisualCaptureEnabled;
            _nativeVisualCaptureEnabledBinding.Update(_nativeVisualCaptureEnabled);

            if (_nativeVisualCaptureEnabled)
            {
                if (_comparisonCitizen == Entity.Null || !EntityManager.Exists(_comparisonCitizen))
                {
                    _nativeVisualCaptureEnabled = false;
                    _nativeVisualCaptureEnabledBinding.Update(false);
                    _nativeVisualStatusBinding.Update("Select a comparison citizen before enabling native LivePath capture.");
                    _statusBinding.Update("Select comparison citizen first, then enable native transport visual capture.");
                    return;
                }
                if (_waitingForPath)
                {
                    _nativeVisualCaptureEnabled = false;
                    _nativeVisualCaptureEnabledBinding.Update(false);
                    _nativeVisualStatusBinding.Update("Wait for the planner path to finish before starting LivePath capture.");
                    _statusBinding.Update("Planner is still calculating. Start native visual capture after the journey text appears.");
                    return;
                }

                EnsureComparisonSession();
                _livePathFingerprints.Clear();
                _nativeVisualChangeNumber = 0;
                CaptureNativeVisualSnapshot("BASELINE", true);
                _nativeVisualStatusBinding.Update("Watching LivePaths sourced by the comparison citizen/current vehicle only.");
                _statusBinding.Update("Native visual capture enabled. Keep that citizen selected normally in CS2 and let them board/transfer.");
            }
            else
            {
                _nativeVisualStatusBinding.Update("Native LivePath capture stopped.");
            }
        }

        private void SnapshotNativeVisual()
        {
            EnsureComparisonSession();
            CaptureNativeVisualSnapshot("MANUAL", true);
            _nativeVisualStatusBinding.Update("Manual native LivePath snapshot written at " + DateTime.Now.ToString("HH:mm:ss"));
        }

        private void UpdateNativeVisualCapture()
        {
            if (!_nativeVisualCaptureEnabled || _waitingForPath)
                return;
            if (_comparisonCitizen == Entity.Null || !EntityManager.Exists(_comparisonCitizen))
                return;

            _nativeVisualFrame++;
            if ((_nativeVisualFrame % NativeVisualSampleInterval) != 0)
                return;

            CaptureNativeVisualSnapshot("AUTO", false);
        }

        private void CaptureNativeVisualSnapshot(string reason, bool forceAll)
        {
            try
            {
                EnsureComparisonSession();
                EntityQuery query = GetEntityQuery(ComponentType.ReadOnly<LivePath>());
                using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.TempJob))
                {
                    int changed = 0;
                    int relevant = 0;
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Entity route = entities[i];
                        if (!IsRelevantLivePath(route))
                            continue;
                        relevant++;
                        string fingerprint = BuildLivePathFingerprint(route);
                        string key = FormatEntity(route);
                        bool isChanged = !_livePathFingerprints.TryGetValue(key, out string previous) || previous != fingerprint;
                        _livePathFingerprints[key] = fingerprint;
                        if (!forceAll && !isChanged)
                            continue;

                        _nativeVisualChangeNumber++;
                        string file = Path.Combine(_comparisonFolder,
                            "native-livepath-" + _nativeVisualChangeNumber.ToString("000", CultureInfo.InvariantCulture) + ".txt");
                        WriteTextSafe(file, BuildLivePathDump(route, reason, isChanged));
                        changed++;
                    }

                    if (forceAll || changed > 0)
                    {
                        string msg = "LivePath scan: " + relevant + " relevant (" + entities.Length + " total); " + changed + " dumped. Change #" + _nativeVisualChangeNumber;
                        _nativeVisualStatusBinding.Update(msg);
                        AppendTextSafe(Path.Combine(_comparisonFolder, "native-livepath-index.txt"),
                            DateTime.Now.ToString("O") + " | " + reason + " | relevant=" + relevant + " | total=" + entities.Length + " | dumped=" + changed + Environment.NewLine);
                    }
                }
            }
            catch (Exception ex)
            {
                _nativeVisualStatusBinding.Update("LivePath capture error: " + ex.GetBaseException().Message);
                Mod.Log.Error("Native LivePath capture failed: " + ex);
            }
        }

        private bool IsRelevantLivePath(Entity route)
        {
            if (_comparisonCitizen == Entity.Null || !EntityManager.Exists(_comparisonCitizen))
                return false;
            if (!EntityManager.HasBuffer<RouteSegment>(route))
                return false;

            Entity currentVehicle = Entity.Null;
            if (EntityManager.HasComponent<CurrentVehicle>(_comparisonCitizen))
                currentVehicle = EntityManager.GetComponentData<CurrentVehicle>(_comparisonCitizen).m_Vehicle;

            DynamicBuffer<RouteSegment> segments = EntityManager.GetBuffer<RouteSegment>(route, true);
            for (int i = 0; i < segments.Length; i++)
            {
                Entity segment = segments[i].m_Segment;
                if (segment == Entity.Null || !EntityManager.Exists(segment) || !EntityManager.HasComponent<PathSource>(segment))
                    continue;
                Entity source = EntityManager.GetComponentData<PathSource>(segment).m_Entity;
                if (source == _comparisonCitizen || (currentVehicle != Entity.Null && source == currentVehicle))
                    return true;
            }
            return false;
        }

        private string BuildLivePathFingerprint(Entity route)
        {
            var b = new StringBuilder();
            b.Append(FormatEntity(route));
            if (EntityManager.HasComponent<RouteBufferIndex>(route))
                b.Append("|rbi=").Append(EntityManager.GetComponentData<RouteBufferIndex>(route).m_Index);
            if (EntityManager.HasBuffer<RouteSegment>(route))
            {
                DynamicBuffer<RouteSegment> segments = EntityManager.GetBuffer<RouteSegment>(route, true);
                b.Append("|segments=").Append(segments.Length);
                for (int i = 0; i < segments.Length; i++)
                {
                    Entity seg = segments[i].m_Segment;
                    b.Append('|').Append(seg.Index).Append(':').Append(seg.Version);
                    if (seg != Entity.Null && EntityManager.Exists(seg))
                    {
                        if (EntityManager.HasBuffer<CurveSource>(seg)) b.Append("cs").Append(EntityManager.GetBuffer<CurveSource>(seg, true).Length);
                        if (EntityManager.HasBuffer<CurveElement>(seg)) b.Append("ce").Append(EntityManager.GetBuffer<CurveElement>(seg, true).Length);
                        if (EntityManager.HasBuffer<PathElement>(seg)) b.Append("pe").Append(EntityManager.GetBuffer<PathElement>(seg, true).Length);
                    }
                }
            }
            return b.ToString();
        }

        private string BuildLivePathDump(Entity route, string reason, bool changed)
        {
            var b = new StringBuilder();
            b.AppendLine("JOURNEY PLANNER v0.3.2 — NATIVE LIVEPATH DISCOVERY");
            b.AppendLine("================================================");
            b.AppendLine("Time: " + DateTime.Now.ToString("O"));
            b.AppendLine("Reason: " + reason);
            b.AppendLine("Changed since last scan: " + changed);
            b.AppendLine("Comparison citizen: " + DescribeEntity(_comparisonCitizen));
            b.AppendLine("Citizen runtime: " + DescribeCitizenRuntimeState());
            b.AppendLine("Planner A: " + DescribeEntity(_origin));
            b.AppendLine("Planner B: " + DescribeEntity(_destination));
            b.AppendLine();
            b.AppendLine("LIVEPATH ROUTE");
            b.AppendLine("--------------");
            b.AppendLine("Entity: " + DescribeEntity(route));
            b.AppendLine("LivePath: " + EntityManager.HasComponent<LivePath>(route));
            if (EntityManager.HasComponent<RouteBufferIndex>(route))
                b.AppendLine("RouteBufferIndex: " + EntityManager.GetComponentData<RouteBufferIndex>(route).m_Index);
            if (EntityManager.HasComponent<Game.Routes.Route>(route))
                b.AppendLine("Route: " + DumpStruct(EntityManager.GetComponentData<Game.Routes.Route>(route)));
            if (EntityManager.HasComponent<Game.Routes.Color>(route))
                b.AppendLine("Color: " + DumpStruct(EntityManager.GetComponentData<Game.Routes.Color>(route)));
            if (EntityManager.HasComponent<Owner>(route))
                b.AppendLine("Owner: " + FormatEntity(EntityManager.GetComponentData<Owner>(route).m_Owner));
            b.AppendLine();

            if (!EntityManager.HasBuffer<RouteSegment>(route))
            {
                b.AppendLine("RouteSegment buffer: <absent>");
                return b.ToString();
            }

            DynamicBuffer<RouteSegment> segments = EntityManager.GetBuffer<RouteSegment>(route, true);
            b.AppendLine("RouteSegment.Length: " + segments.Length);
            for (int i = 0; i < segments.Length; i++)
            {
                Entity seg = segments[i].m_Segment;
                b.AppendLine();
                b.AppendLine("SEGMENT [" + i + "] " + DescribeEntity(seg));
                b.AppendLine("----------------------------------------");
                if (seg == Entity.Null || !EntityManager.Exists(seg))
                {
                    b.AppendLine("<missing>");
                    continue;
                }

                if (EntityManager.HasComponent<PathSource>(seg))
                {
                    PathSource source = EntityManager.GetComponentData<PathSource>(seg);
                    b.AppendLine("PathSource: " + DumpStruct(source));
                    b.AppendLine("PathSource entity: " + DescribeEntity(source.m_Entity));
                    b.AppendLine("PathSource == comparison citizen: " + (source.m_Entity == _comparisonCitizen));
                }
                else b.AppendLine("PathSource: <absent>");

                if (EntityManager.HasComponent<Owner>(seg))
                    b.AppendLine("Owner: " + DescribeEntity(EntityManager.GetComponentData<Owner>(seg).m_Owner));
                if (EntityManager.HasComponent<Game.Routes.Segment>(seg))
                    b.AppendLine("Segment component: " + DumpStruct(EntityManager.GetComponentData<Game.Routes.Segment>(seg)));

                if (EntityManager.HasBuffer<CurveSource>(seg))
                {
                    DynamicBuffer<CurveSource> buffer = EntityManager.GetBuffer<CurveSource>(seg, true);
                    b.AppendLine("CurveSource.Length: " + buffer.Length);
                    for (int n = 0; n < buffer.Length; n++)
                        b.AppendLine("  [" + n + "] " + DumpStruct(buffer[n]));
                }
                else b.AppendLine("CurveSource: <absent>");

                if (EntityManager.HasBuffer<CurveElement>(seg))
                {
                    DynamicBuffer<CurveElement> buffer = EntityManager.GetBuffer<CurveElement>(seg, true);
                    b.AppendLine("CurveElement.Length: " + buffer.Length);
                    for (int n = 0; n < buffer.Length; n++)
                        b.AppendLine("  [" + n + "] " + DumpStruct(buffer[n]));
                }
                else b.AppendLine("CurveElement: <absent>");

                if (EntityManager.HasBuffer<PathElement>(seg))
                {
                    DynamicBuffer<PathElement> buffer = EntityManager.GetBuffer<PathElement>(seg, true);
                    b.AppendLine("PathElement.Length: " + buffer.Length);
                    for (int n = 0; n < buffer.Length; n++)
                    {
                        PathElement pe = buffer[n];
                        b.AppendLine("  [" + n + "] " + FormatEntity(pe.m_Target) + " | " + ResolveName(pe.m_Target) + " | delta=" + pe.m_TargetDelta);
                    }
                }
                else b.AppendLine("PathElement: <absent>");
            }

            return b.ToString();
        }

        private string DescribeCitizenRuntimeState()
        {
            if (_comparisonCitizen == Entity.Null || !EntityManager.Exists(_comparisonCitizen))
                return "<no comparison citizen>";

            var b = new StringBuilder();
            if (EntityManager.HasComponent<Game.Creatures.Resident>(_comparisonCitizen))
                b.Append("Resident=").Append(DumpStruct(EntityManager.GetComponentData<Game.Creatures.Resident>(_comparisonCitizen))).Append("; ");
            if (EntityManager.HasComponent<PathOwner>(_comparisonCitizen))
            {
                PathOwner po = EntityManager.GetComponentData<PathOwner>(_comparisonCitizen);
                b.Append("PathOwner[state=").Append(po.m_State).Append(",index=").Append(po.m_ElementIndex).Append("]; ");
            }
            if (EntityManager.HasComponent<CurrentVehicle>(_comparisonCitizen))
                b.Append("CurrentVehicle=").Append(DumpStruct(EntityManager.GetComponentData<CurrentVehicle>(_comparisonCitizen))).Append("; ");
            if (EntityManager.HasComponent<HumanCurrentLane>(_comparisonCitizen))
                b.Append("HumanCurrentLane=").Append(DumpStruct(EntityManager.GetComponentData<HumanCurrentLane>(_comparisonCitizen))).Append("; ");
            return b.ToString();
        }

        private static string DumpStruct<T>(T value) where T : struct
        {
            object boxed = value;
            Type type = boxed.GetType();
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fields.Length == 0) return type.Name;
            var b = new StringBuilder();
            b.Append(type.Name).Append("{");
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) b.Append(", ");
                object fieldValue;
                try { fieldValue = fields[i].GetValue(boxed); }
                catch { fieldValue = "<unreadable>"; }
                b.Append(fields[i].Name).Append('=').Append(fieldValue ?? "null");
            }
            b.Append('}');
            return b.ToString();
        }

        private void ToggleRoute()
        {
            _routeVisible = !_routeVisible;
            _routeVisibleBinding.Update(_routeVisible);
            _routeRenderer.Visible = _routeVisible;
        }

        private void DeleteRoute()
        {
            DestroyProbe();
            _waitingForPath = false;
            _busyBinding.Update(false);

            DestroyRouteOverlay();

            // Keep A/B so the user can immediately rebuild the same journey.
            _routeVisible = true;
            _routeVisibleBinding.Update(true);
            _resultBinding.Update("No journey calculated yet.");
            _journeyJsonBinding.Update("{\"ready\":false}");
            _statusBinding.Update("Route deleted. Start and destination were kept.");
        }

        private void EnsureComparisonSession()
        {
            if (!String.IsNullOrWhiteSpace(_comparisonFolder))
                return;

            string root = @"C:\Temp\JourneyPlanner\Comparisons";
            string session = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            _comparisonFolder = Path.Combine(root, session);
            Directory.CreateDirectory(_comparisonFolder);
            _comparisonFolderBinding.Update(_comparisonFolder);

            var header = new StringBuilder();
            header.AppendLine("JOURNEY PLANNER COMPARISON SESSION");
            header.AppendLine("Created: " + DateTime.Now.ToString("O"));
            header.AppendLine("Origin: " + DescribeEntity(_origin));
            header.AppendLine("Destination: " + DescribeEntity(_destination));
            header.AppendLine("Citizen: " + DescribeEntity(_comparisonCitizen));
            WriteTextSafe(Path.Combine(_comparisonFolder, "session.txt"), header.ToString());

            WriteTextSafe(Path.Combine(_comparisonFolder, "actual-timeline.csv"),
                "time,citizen,path_state,path_index,path_length,current_target,current_name,mode,route_owner,route_number,waypoint\r\n");
        }

        private void UpdateComparisonCapture()
        {
            if (!_comparisonEnabled)
                return;
            if (_comparisonCitizen == Entity.Null || !EntityManager.Exists(_comparisonCitizen))
                return;

            EnsureComparisonSession();
            _comparisonFrame++;
            if ((_comparisonFrame % ComparisonSampleInterval) != 0)
                return;

            PathOwner owner = EntityManager.HasComponent<PathOwner>(_comparisonCitizen)
                ? EntityManager.GetComponentData<PathOwner>(_comparisonCitizen)
                : default(PathOwner);

            int length = 0;
            Entity currentTarget = Entity.Null;
            TransitInfo currentTransit = default;
            string currentName = "";

            if (EntityManager.HasBuffer<PathElement>(_comparisonCitizen))
            {
                DynamicBuffer<PathElement> path = EntityManager.GetBuffer<PathElement>(_comparisonCitizen, true);
                length = path.Length;
                int index = owner.m_ElementIndex;
                if (index >= 0 && index < path.Length)
                {
                    currentTarget = path[index].m_Target;
                    currentName = ResolveName(currentTarget);
                    currentTransit = GetTransitInfo(currentTarget);
                }

                string fingerprint = BuildPathFingerprint(owner, path);
                if (fingerprint != _lastCitizenPathFingerprint)
                {
                    _lastCitizenPathFingerprint = fingerprint;
                    AppendCitizenPathSnapshot(owner, path);
                }
            }

            string mode = currentTransit.IsTransit ? currentTransit.Mode : "Walk";
            string route = currentTransit.RouteOwner == Entity.Null ? "" : FormatEntity(currentTransit.RouteOwner);
            string number = currentTransit.RouteNumber >= 0 ? currentTransit.RouteNumber.ToString(CultureInfo.InvariantCulture) : "";
            string waypoint = currentTransit.WaypointIndex >= 0 ? currentTransit.WaypointIndex.ToString(CultureInfo.InvariantCulture) : "";

            string csv = Csv(DateTime.Now.ToString("O")) + "," +
                         Csv(FormatEntity(_comparisonCitizen)) + "," +
                         Csv(owner.m_State.ToString()) + "," +
                         owner.m_ElementIndex.ToString(CultureInfo.InvariantCulture) + "," +
                         length.ToString(CultureInfo.InvariantCulture) + "," +
                         Csv(FormatEntity(currentTarget)) + "," + Csv(currentName) + "," +
                         Csv(mode) + "," + Csv(route) + "," + Csv(number) + "," + Csv(waypoint) + "\r\n";
            AppendTextSafe(Path.Combine(_comparisonFolder, "actual-timeline.csv"), csv);
        }

        private string BuildPathFingerprint(PathOwner owner, DynamicBuffer<PathElement> path)
        {
            var b = new StringBuilder();
            b.Append(owner.m_State).Append('|').Append(owner.m_ElementIndex).Append('|').Append(path.Length);
            for (int i = 0; i < path.Length; i++)
                b.Append('|').Append(path[i].m_Target.Index).Append(':').Append(path[i].m_Target.Version);
            return b.ToString();
        }

        private void AppendCitizenPathSnapshot(PathOwner owner, DynamicBuffer<PathElement> path)
        {
            var b = new StringBuilder();
            b.AppendLine();
            b.AppendLine("============================================================");
            b.AppendLine("TIME: " + DateTime.Now.ToString("O"));
            b.AppendLine("Citizen: " + DescribeEntity(_comparisonCitizen));
            b.AppendLine("PathOwner: state=" + owner.m_State + " index=" + owner.m_ElementIndex);
            b.AppendLine("PathElement.Length: " + path.Length);
            for (int i = 0; i < path.Length; i++)
            {
                Entity target = path[i].m_Target;
                TransitInfo info = GetTransitInfo(target);
                b.Append("[").Append(i).Append("] ").Append(FormatEntity(target)).Append(" | ").Append(ResolveName(target));
                if (info.IsTransit)
                {
                    b.Append(" | ").Append(info.Mode)
                     .Append(" route=").Append(FormatEntity(info.RouteOwner))
                     .Append(" number=").Append(info.RouteNumber)
                     .Append(" waypoint=").Append(info.WaypointIndex);
                }
                if (i == owner.m_ElementIndex) b.Append("  <== CURRENT");
                b.AppendLine();
            }
            AppendTextSafe(Path.Combine(_comparisonFolder, "actual-path-snapshots.txt"), b.ToString());
        }

        private void WritePlannerComparisonFile(DynamicBuffer<PathElement> path, string result)
        {
            if (!_comparisonEnabled)
                return;
            EnsureComparisonSession();

            var b = new StringBuilder();
            b.AppendLine(result);
            b.AppendLine();
            b.AppendLine("RAW PLANNER PATH");
            b.AppendLine("----------------");
            for (int i = 0; i < path.Length; i++)
            {
                TransitInfo info = GetTransitInfo(path[i].m_Target);
                b.Append("[").Append(i).Append("] ").Append(FormatEntity(path[i].m_Target)).Append(" | ").Append(ResolveName(path[i].m_Target));
                if (info.IsTransit)
                    b.Append(" | ").Append(info.Mode).Append(" route=").Append(FormatEntity(info.RouteOwner)).Append(" number=").Append(info.RouteNumber).Append(" waypoint=").Append(info.WaypointIndex);
                b.AppendLine();
            }
            WriteTextSafe(Path.Combine(_comparisonFolder, "planner-proposed.txt"), b.ToString());
        }

        private List<JourneyLeg> PrepareJourney(DynamicBuffer<PathElement> path)
        {
            List<JourneyLeg> legs = BuildLegs(path);
            foreach (JourneyLeg leg in legs)
            {
                leg.DistanceMeters = leg.Mode == "Walk" || IsPrivateMode(leg.Mode) ? CalculateLegDistance(path, leg) : 0f;
                leg.RenderColor = GetLegColor(leg);
                leg.ColorHex = ColorHex(leg.RenderColor);
                leg.RouteName = GetRouteDisplayName(leg);
            }
            return legs;
        }

        private void RenderRoute(DynamicBuffer<PathElement> path, List<JourneyLeg> legs)
        {
            DestroyRouteOverlay();
            _journeyLegs = legs;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (int n = 0; n < legs.Count; n++)
            {
                JourneyLeg leg = legs[n];
                if (leg.Mode == "Walk" || IsPrivateMode(leg.Mode))
                {
                    for (int i = leg.StartPathIndex; i <= leg.EndPathIndex && i < path.Length; i++)
                        AddRouteElement(path[i], leg, n, i, i);
                }
                else
                {
                    RenderTransitLeg(leg, n);
                }
            }
            _routeRenderer.Visible = _routeVisible;
            Mod.Log.Info("Route overlay cached: " + _routePieces.Count + " curves, " + legs.Count +
                         " legs in " + timer.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms.");
        }

        private void RenderTransitLeg(JourneyLeg leg, int legIndex)
        {
            Entity route = leg.RouteOwner;
            if (route == Entity.Null || !EntityManager.Exists(route) ||
                !EntityManager.HasBuffer<RouteSegment>(route) || !EntityManager.HasBuffer<RouteWaypoint>(route)) return;
            var segments = EntityManager.GetBuffer<RouteSegment>(route, true);
            var waypoints = EntityManager.GetBuffer<RouteWaypoint>(route, true);
            if (segments.Length == 0 || waypoints.Length == 0 || leg.FirstWaypoint < 0 || leg.LastWaypoint < 0) return;
            int first = leg.FirstWaypoint % waypoints.Length;
            int last = leg.LastWaypoint % waypoints.Length;
            if (first == last) return; // Boarding and alighting here does not mean a full route loop.
            UnityEngine.Color color = leg.RenderColor;
            for (int hop = 0, current = first; hop < waypoints.Length && current != last; hop++)
            {
                if (current >= segments.Length) break;
                AddStopMarker(waypoints[current].m_Waypoint, color, legIndex, _routePieces.Count);
                Entity segment = segments[current].m_Segment;
                if (EntityManager.Exists(segment))
                {
                    bool hasGeometry = false;
                    if (EntityManager.HasBuffer<PathElement>(segment))
                    {
                        var segmentPath = EntityManager.GetBuffer<PathElement>(segment, true);
                        for (int i = 0; i < segmentPath.Length; i++)
                            hasGeometry |= AddRouteElement(segmentPath[i], leg, legIndex, leg.StartPathIndex, leg.EndPathIndex);
                    }
                    // The segment curve is only a fallback, never a duplicate of its lane path.
                    if (!hasGeometry && TryGetBezier(segment, out Bezier4x3 curve))
                        AddRouteCurve(curve, leg, legIndex, leg.StartPathIndex, leg.EndPathIndex);
                }
                current = (current + 1) % waypoints.Length;
            }
            AddStopMarker(waypoints[last].m_Waypoint, color, legIndex, _routePieces.Count);
        }

        private bool AddRouteElement(PathElement element, JourneyLeg leg, int legIndex, int start, int end)
        {
            if (!TryGetBezier(element.m_Target, out Bezier4x3 curve)) return false;
            // TargetDelta is the native traversal interval, including reversed and partial lanes.
            if (!JourneyGeometry.TryTrim(curve, element.m_TargetDelta, out curve)) return false;
            AddRouteCurve(curve, leg, legIndex, start, end);
            return true;
        }

        private void AddRouteCurve(Bezier4x3 curve, JourneyLeg leg, int legIndex, int start, int end)
        {
            _routeRenderer.AddCurve(new JourneyPlannerRouteSystem.RouteCurve
            {
                Curve = curve, Color = leg.RenderColor, LegIndex = legIndex,
                Width = leg.Mode == "Walk" ? 2.2f : 4f
            });
            _routePieces.Add(new RoutePiece { Curve = curve, JourneyStartIndex = start, JourneyEndIndex = end });
        }

        private void AddStopMarker(Entity waypoint, UnityEngine.Color color, int legIndex, int curveIndex)
        {
            Entity target = waypoint;
            if (EntityManager.Exists(waypoint) && EntityManager.HasComponent<Connected>(waypoint))
                target = EntityManager.GetComponentData<Connected>(waypoint).m_Connected;
            if (!TryGetWorldPosition(target, out float3 position) && !TryGetWorldPosition(waypoint, out position)) return;
            _routeRenderer.AddStop(new JourneyPlannerRouteSystem.StopMarker
            { Position = position, Color = color, LegIndex = legIndex, CurveIndex = curveIndex });
        }

        private UnityEngine.Color GetLegColor(JourneyLeg leg)
        {
            if (leg.RouteOwner != Entity.Null && EntityManager.Exists(leg.RouteOwner) &&
                EntityManager.HasComponent<Game.Routes.Color>(leg.RouteOwner))
            {
                UnityEngine.Color color = EntityManager.GetComponentData<Game.Routes.Color>(leg.RouteOwner).m_Color;
                color.a = 1f;
                return color;
            }
            return GetModeColor(leg.Mode);
        }

        private static string ColorHex(UnityEngine.Color color)
        {
            Color32 bytes = color;
            return String.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", bytes.r, bytes.g, bytes.b);
        }

        private string GetRouteDisplayName(JourneyLeg leg)
        {
            if (leg.RouteOwner == Entity.Null || !EntityManager.Exists(leg.RouteOwner)) return "";
            if (_nameSystem.TryGetCustomName(leg.RouteOwner, out string custom) && !String.IsNullOrWhiteSpace(custom)) return custom;
            return leg.Mode + " Line" + (leg.RouteNumber >= 0 ? " " + leg.RouteNumber : "");
        }

        private void RefreshRouteColors()
        {
            if (_waitingForPath || _journeyLegs == null || (!_isOpen && !_routeVisible) || UnityEngine.Time.realtimeSinceStartup < _nextColorRefresh) return;
            _nextColorRefresh = UnityEngine.Time.realtimeSinceStartup + 1f;
            bool changed = false;
            for (int i = 0; i < _journeyLegs.Count; i++)
            {
                JourneyLeg leg = _journeyLegs[i];
                if (leg.RouteOwner == Entity.Null) continue;
                UnityEngine.Color color = GetLegColor(leg);
                string hex = ColorHex(color);
                string name = GetRouteDisplayName(leg);
                if (hex == leg.ColorHex && name == leg.RouteName) continue;
                leg.ColorHex = hex;
                leg.RenderColor = color;
                leg.RouteName = name;
                _routeRenderer.SetLegColor(i, color);
                changed = true;
            }
            if (changed) _journeyJsonBinding.Update(BuildJourneyJson(_journeyLegs));
        }

        private List<JourneyLeg> BuildLegs(DynamicBuffer<PathElement> path)
        {
            var legs = new List<JourneyLeg>();
            JourneyLeg current = null;
            string privateMode = null;

            for (int i = 0; i < path.Length; i++)
            {
                PathElement element = path[i];
                Entity target = element.m_Target;
                TransitInfo transit = GetTransitInfo(target);
                string name = ResolveName(target);
                string lower = (name ?? "").ToLowerInvariant();

                string mode;
                string key;
                Entity routeOwner = Entity.Null;
                int routeNumber = -1;
                int waypoint = -1;
                int exitWaypoint = -1;

                if (transit.IsTransit)
                {
                    privateMode = null;
                    mode = transit.Mode;
                    routeOwner = transit.RouteOwner;
                    routeNumber = transit.RouteNumber;
                    waypoint = transit.WaypointIndex;
                    exitWaypoint = transit.ExitWaypointIndex;
                    key = mode + ":" + routeOwner.Index + ":" + routeOwner.Version;
                }
                else
                {
                    // Explicit vehicle marker embedded by the citizen AI.
                    if (lower == "motorcycle" || lower.Contains("motorcycle"))
                        privateMode = "Motorcycle";
                    else if (lower == "bicycle" || lower.Contains("bicycle"))
                        privateMode = "Bike";
                    else if (lower.Contains("personal car"))
                        privateMode = "Car";

                    bool pedestrian = IsPedestrianPathName(lower);
                    bool roadish = IsRoadVehiclePathName(lower);

                    // A pedestrian lane/access/spawn after a road run means the private
                    // vehicle leg is over. Connection lanes before the vehicle marker
                    // remain walking; connection lanes after it remain part of vehicle
                    // access until a definite pedestrian element appears.
                    if (privateMode != null && pedestrian)
                        privateMode = null;

                    if (privateMode != null && (roadish || lower.Contains("connection lane") ||
                                                lower.Contains("motorcycle") || lower.Contains("bicycle") ||
                                                lower.Contains("personal car")))
                    {
                        mode = privateMode;
                    }
                    else if (!_citizenOriginMode && (_activeRequestMode == "Car" || _activeRequestMode == "Bike") &&
                             roadish)
                    {
                        mode = _activeRequestMode;
                    }
                    else
                    {
                        mode = "Walk";
                    }
                    key = mode;
                }

                if (current == null || current.Key != key)
                {
                    current = new JourneyLeg
                    {
                        Key = key, Mode = mode, RouteOwner = routeOwner,
                        StartPathIndex = i, EndPathIndex = i, FirstTarget = target, LastTarget = target,
                        RouteNumber = routeNumber, FirstWaypoint = waypoint, LastWaypoint = exitWaypoint
                    };
                    legs.Add(current);
                }
                else
                {
                    current.EndPathIndex = i;
                    current.LastTarget = target;
                    if (exitWaypoint >= 0) current.LastWaypoint = exitWaypoint;
                }
            }
            return legs;
        }

        private static bool IsPrivateMode(string mode)
        {
            return mode == "Car" || mode == "Bike" || mode == "Motorcycle";
        }

        private static bool IsPedestrianPathName(string lower)
        {
            if (String.IsNullOrEmpty(lower)) return false;
            return lower.Contains("pedestrian lane") ||
                   lower.Contains("pedestrian access") ||
                   lower.Contains("pedestrian spawn") ||
                   lower.Contains("crosswalk") ||
                   lower.Contains("sidewalk");
        }

        private static bool IsRoadVehiclePathName(string lower)
        {
            if (String.IsNullOrEmpty(lower)) return false;
            return lower.Contains("car drive lane") ||
                   lower.Contains("car lane") ||
                   lower.Contains("parking lane") ||
                   lower.Contains("road lane") ||
                   lower.Contains("bike lane") ||
                   lower.Contains("bicycle lane") ||
                   lower.Contains("invisible car");
        }

        private UnityEngine.Color GetModeColor(string mode)
        {
            switch ((mode ?? "").ToLowerInvariant())
            {
                case "walk": return new UnityEngine.Color(0.65f, 0.70f, 0.78f, 1f);
                case "bus": return new UnityEngine.Color(0.15f, 0.65f, 1f, 1f);
                case "tram": return new UnityEngine.Color(0.25f, 0.9f, 0.45f, 1f);
                case "metro": return new UnityEngine.Color(0.75f, 0.35f, 1f, 1f);
                case "train": return new UnityEngine.Color(1f, 0.55f, 0.15f, 1f);
                case "car": return new UnityEngine.Color(1f, 0.72f, 0.10f, 1f);
                case "motorcycle": return new UnityEngine.Color(1f, 0.45f, 0.12f, 1f);
                case "bike": return new UnityEngine.Color(0.35f, 1f, 0.35f, 1f);
                default: return new UnityEngine.Color(0.05f, 0.85f, 1f, 1f);
            }
        }

        private bool TryGetBezier(Entity entity, out Bezier4x3 curve)
        {
            curve = default;
            if (entity == Entity.Null || !EntityManager.Exists(entity) || !EntityManager.HasComponent<Game.Net.Curve>(entity)) return false;
            curve = EntityManager.GetComponentData<Game.Net.Curve>(entity).m_Bezier;
            return true;
        }

        private bool TryGetWorldPosition(Entity entity, out float3 position)
        {
            position = float3.zero;
            Entity current = entity;
            for (int depth = 0; depth < 6; depth++)
            {
                if (current == Entity.Null || !EntityManager.Exists(current)) break;
                if (EntityManager.HasComponent<Game.Objects.Transform>(current))
                {
                    position = EntityManager.GetComponentData<Game.Objects.Transform>(current).m_Position;
                    return true;
                }
                if (EntityManager.HasComponent<Game.Routes.Position>(current))
                {
                    position = EntityManager.GetComponentData<Game.Routes.Position>(current).m_Position;
                    return true;
                }
                if (EntityManager.HasComponent<Connected>(current))
                {
                    Entity next = EntityManager.GetComponentData<Connected>(current).m_Connected;
                    if (next != Entity.Null && next != current) { current = next; continue; }
                }
                if (EntityManager.HasComponent<Owner>(current))
                {
                    Entity next = EntityManager.GetComponentData<Owner>(current).m_Owner;
                    if (next != Entity.Null && next != current) { current = next; continue; }
                }
                break;
            }
            return false;
        }

        private Entity ResolveCitizenFromEntity(Entity entity)
        {
            Entity current = entity;
            for (int depth = 0; depth < 12; depth++)
            {
                if (current == Entity.Null || !EntityManager.Exists(current))
                    return Entity.Null;
                if (IsCitizenEntity(current))
                    return current;
                if (!EntityManager.HasComponent<Owner>(current))
                    return Entity.Null;
                Entity owner = EntityManager.GetComponentData<Owner>(current).m_Owner;
                if (owner == Entity.Null || owner == current)
                    return Entity.Null;
                current = owner;
            }
            return Entity.Null;
        }

        private bool IsCitizenEntity(Entity entity)
        {
            return entity != Entity.Null && EntityManager.Exists(entity) &&
                   (EntityManager.HasComponent<Human>(entity) ||
                    EntityManager.HasComponent<Game.Creatures.Resident>(entity));
        }

        private Entity ResolveCitizenDestination(Entity citizen)
        {
            if (!IsCitizenEntity(citizen) || !EntityManager.HasComponent<Game.Common.Target>(citizen))
                return Entity.Null;

            Entity target = EntityManager.GetComponentData<Game.Common.Target>(citizen).m_Target;
            if (target == Entity.Null || !EntityManager.Exists(target))
                return Entity.Null;

            // A citizen Target is commonly already the destination building. If it is
            // an attachment/property child, walk Owner links until we reach a target
            // that CurrentLocation pathfinding can use safely.
            Entity current = target;
            for (int depth = 0; depth < 12; depth++)
            {
                if (current == Entity.Null || !EntityManager.Exists(current)) break;
                if (EntityManager.HasComponent<Game.Buildings.Building>(current) || IsCitizenEntity(current))
                    return current;
                if (!EntityManager.HasComponent<Owner>(current)) break;
                Entity owner = EntityManager.GetComponentData<Owner>(current).m_Owner;
                if (owner == Entity.Null || owner == current) break;
                current = owner;
            }

            // Some valid simulation destinations are not Building components. Return
            // the direct Target only if it has a world transform; otherwise require B
            // to be selected manually rather than sending another bad path request.
            if (EntityManager.HasComponent<Game.Objects.Transform>(target))
                return target;
            return Entity.Null;
        }

        private void UpdateCitizenRouteProgress()
        {
            if (_followCitizen == Entity.Null || _routePieces.Count == 0 || !_routeVisible || _followPieceIndex >= _routePieces.Count) return;
            if (!EntityManager.Exists(_followCitizen)) { _followCitizen = Entity.Null; return; }
            if (++_followFrame < FollowUpdateInterval) return;
            _followFrame = 0;
            if (!TryGetFollowerPosition(_followCitizen, out float3 world)) return;
            if (_hasFollowPosition && math.distancesq(world, _lastFollowPosition) < 0.04f) return;
            _hasFollowPosition = true;
            _lastFollowPosition = world;
            int bestPiece = -1, bestPoint = -1;
            float bestDistanceSq = FollowAcquireDistance * FollowAcquireDistance;
            for (int p = _followPieceIndex; p < _routePieces.Count; p++)
            {
                RoutePiece piece = _routePieces[p];
                for (int i = p == _followPieceIndex ? _followPointIndex : 0; i <= CurveSamples; i++)
                {
                    float3 point = MathUtils.Position(piece.Curve, i / (float)CurveSamples);
                    float distanceSq = math.distancesq(point.xz, world.xz);
                    if (distanceSq < bestDistanceSq)
                    {
                        bestDistanceSq = distanceSq;
                        bestPiece = p;
                        bestPoint = i;
                    }
                }
                if (bestPiece == p && bestDistanceSq < 18f * 18f) break;
            }
            if (bestPiece < 0) return;
            _followPieceIndex = bestPiece;
            _followPointIndex = bestPoint;
            if (_followPointIndex >= CurveSamples)
            {
                _followPieceIndex++;
                _followPointIndex = 0;
            }
            _routeRenderer.SetProgress(_followPieceIndex, _followPointIndex / (float)CurveSamples);
        }

        private bool TryGetFollowerPosition(Entity citizen, out float3 position)
        {
            if (EntityManager.HasComponent<CurrentVehicle>(citizen))
            {
                Entity vehicle = EntityManager.GetComponentData<CurrentVehicle>(citizen).m_Vehicle;
                if (vehicle != Entity.Null && EntityManager.Exists(vehicle) && TryGetWorldPosition(vehicle, out position)) return true;
            }
            return TryGetWorldPosition(citizen, out position);
        }

        private void DestroyRouteOverlay()
        {
            _routeRenderer?.ClearRoute();
            _routePieces.Clear();
            _journeyLegs = null;
            _followPieceIndex = 0;
            _followPointIndex = 0;
            _followFrame = 0;
            _hasFollowPosition = false;
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static void WriteTextSafe(string path, string text)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text, Encoding.UTF8); }
            catch (Exception ex) { Mod.Log.Error("Comparison log write failed: " + ex); }
        }

        private static void AppendTextSafe(string path, string text)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.AppendAllText(path, text, Encoding.UTF8); }
            catch (Exception ex) { Mod.Log.Error("Comparison log append failed: " + ex); }
        }

        private void FinishFailure(string message)
        {
            _waitingForPath = false;
            _busyBinding.Update(false);
            _statusBinding.Update(message);
            _resultBinding.Update(message);
            _journeyJsonBinding.Update("{\"ready\":false}");
            DestroyProbe();
            DestroyRouteOverlay();
        }

        private void Clear()
        {
            CancelSelection();
            DestroyProbe();
            _origin = Entity.Null;
            _destination = Entity.Null;
            _followCitizen = Entity.Null;
            _citizenOriginMode = false;
            _citizenOriginBinding.Update(false);
            _awaitingDestination = false;
            _awaitingDestinationBinding.Update(false);
            _comparisonCitizen = Entity.Null;
            _comparisonCitizenBinding.Update("No citizen selected");
            _waitingForPath = false;
            _busyBinding.Update(false);
            _originBinding.Update("Not selected");
            _destinationBinding.Update("Not selected");
            _resultBinding.Update("No journey calculated yet.");
            _journeyJsonBinding.Update("{\"ready\":false}");
            _statusBinding.Update("Choose origin A and destination B.");
            DestroyRouteOverlay();
            _livePathFingerprints.Clear();
            if (_isOpen)
                ResumeAutoSelection();
        }

        private void DestroyProbe()
        {
            _waitingForPath = false;
            _busyBinding?.Update(false);
            if (_probe != Entity.Null && EntityManager.Exists(_probe))
            {
                try { EntityManager.DestroyEntity(_probe); }
                catch { }
            }
            _probe = Entity.Null;
        }

        protected override void OnDestroy()
        {
            DestroyProbe();
            DestroyRouteOverlay();
            base.OnDestroy();
        }

        private string DescribeEntity(Entity entity) => FormatEntity(entity) + " | " + ResolveName(entity);

        private string ResolveName(Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
                return "(invalid entity)";
            try
            {
                string rendered = _nameSystem.GetRenderedLabelName(entity);
                if (!String.IsNullOrWhiteSpace(rendered)) return rendered;
                if (_nameSystem.TryGetCustomName(entity, out string custom) && !String.IsNullOrWhiteSpace(custom)) return custom;
            }
            catch { }
            return "(no rendered name)";
        }

        private static string FormatPosition(float3 p) =>
            p.x.ToString("0.0", CultureInfo.InvariantCulture) + ", " +
            p.y.ToString("0.0", CultureInfo.InvariantCulture) + ", " +
            p.z.ToString("0.0", CultureInfo.InvariantCulture);

        private static string FormatEntity(Entity e) => e == Entity.Null ? "Entity(0:0)" : "Entity(" + e.Index + ":" + e.Version + ")";

        private enum SelectionMode
        {
            None,
            Auto,
            Origin,
            Destination,
            ComparisonCitizen
        }

        private sealed class RoutePiece
        {
            public Bezier4x3 Curve;
            public int JourneyStartIndex;
            public int JourneyEndIndex;
        }

        private sealed class JourneyLeg
        {
            public float DistanceMeters;
            public UnityEngine.Color RenderColor;
            public string ColorHex;
            public string RouteName;
            public string Key;
            public string Mode;
            public Entity RouteOwner;
            public int RouteNumber;
            public int StartPathIndex;
            public int EndPathIndex;
            public Entity FirstTarget;
            public Entity LastTarget;
            public int FirstWaypoint;
            public int LastWaypoint;
        }

        private struct TransitInfo
        {
            public bool IsTransit;
            public string Mode;
            public Entity RouteOwner;
            public int RouteNumber;
            public int WaypointIndex;
            public int ExitWaypointIndex;
        }
    }
}
