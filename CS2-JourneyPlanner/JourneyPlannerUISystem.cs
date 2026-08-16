using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Text;
using Colossal.UI.Binding;
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
        private const float RouteHeightOffset = 0.65f;

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
        private GameObject _routeRoot;
        private Material _routeMaterial;

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
                    string result = ParseJourney(path);
                    _lastPlannerResult = result;
                    _resultBinding.Update(result);
                    _journeyJsonBinding.Update(BuildJourneyJson(path));
                    RenderRoute(path);
                    _statusBinding.Update("Journey ready.");
                    _waitingForPath = false;
                    _busyBinding.Update(false);
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

        private void Close()
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

            bool selected = false;
            bool followInvoked = false;
            try
            {
                // First restore the vanilla selected entity. This re-opens/reconnects
                // CS2's standard info-panel state without a compile-time dependency
                // on a particular ToolSystem.selected setter.
                PropertyInfo selectedProp = _toolSystem.GetType().GetProperty(
                    "selected", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (selectedProp != null && selectedProp.CanWrite)
                {
                    selectedProp.SetValue(_toolSystem, citizen, null);
                    selected = true;
                }

                // CS2 builds have moved camera/follow helpers between systems. Search
                // existing managed systems at runtime for a narrowly named follow/focus
                // method taking one Entity. If none exists, vanilla selection is still
                // restored and the normal info-panel follow control remains available.
                PropertyInfo systemsProp = typeof(World).GetProperty(
                    "Systems", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object systemsObj = systemsProp != null ? systemsProp.GetValue(World, null) : null;
                System.Collections.IEnumerable systems = systemsObj as System.Collections.IEnumerable;
                if (systems != null)
                {
                    foreach (object sys in systems)
                    {
                        if (sys == null) continue;
                        string tn = sys.GetType().FullName ?? "";
                        if (tn.IndexOf("Camera", StringComparison.OrdinalIgnoreCase) < 0 &&
                            tn.IndexOf("Follow", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        foreach (MethodInfo m in sys.GetType().GetMethods(
                                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            string mn = m.Name ?? "";
                            if (mn.IndexOf("Follow", StringComparison.OrdinalIgnoreCase) < 0 &&
                                mn.IndexOf("Focus", StringComparison.OrdinalIgnoreCase) < 0)
                                continue;
                            ParameterInfo[] ps = m.GetParameters();
                            if (ps.Length == 1 && ps[0].ParameterType == typeof(Entity))
                            {
                                try
                                {
                                    m.Invoke(sys, new object[] { citizen });
                                    followInvoked = true;
                                    break;
                                }
                                catch { }
                            }
                        }
                        if (followInvoked) break;
                    }
                }
            }
            catch (Exception ex)
            {
                Mod.Log.Warn("Re-follow best-effort call failed: " + ex.GetBaseException().Message);
            }

            _statusBinding.Update(followInvoked
                ? "Re-followed citizen using the native camera/follow system."
                : selected
                    ? "Citizen re-selected in the vanilla UI. If your build does not expose the follow method, use the vanilla follow icon once."
                    : "Could not invoke the native follow control on this CS2 build.");
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
            string result = ParseJourney(path);
            _lastPlannerResult = result;
            _resultBinding.Update(result);
            _journeyJsonBinding.Update(BuildJourneyJson(path));
            RenderRoute(path);
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
                if (!TryGetCurvePoints(path[i].m_Target, out Vector3[] points) || points == null)
                    continue;
                for (int n = 1; n < points.Length; n++)
                    total += Vector3.Distance(points[n - 1], points[n]);
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

        private string BuildJourneyJson(DynamicBuffer<PathElement> path)
        {
            List<JourneyLeg> legs = BuildLegs(path);
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
                float distance = (leg.Mode == "Walk" || IsPrivateMode(leg.Mode))
                    ? CalculateLegDistance(path, leg)
                    : 0f;
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

        private string ParseJourney(DynamicBuffer<PathElement> path)
        {
            List<JourneyLeg> legs = BuildLegs(path);

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

            string name = ResolveName(target);
            string lower = name.ToLowerInvariant();
            if (lower.Contains("bus line")) info.Mode = "Bus";
            else if (lower.Contains("tram line")) info.Mode = "Tram";
            else if (lower.Contains("passenger railway") || lower.Contains("train line")) info.Mode = "Train";
            else if (lower.Contains("metro line") || lower.Contains("subway line")) info.Mode = "Metro";
            else if (lower.Contains("ship line") || lower.Contains("ferry line")) info.Mode = "Ship";
            else if (lower.Contains("airplane line") || lower.Contains("air line")) info.Mode = "Air";
            else return info;

            info.IsTransit = true;
            if (EntityManager.HasComponent<Owner>(target))
                info.RouteOwner = EntityManager.GetComponentData<Owner>(target).m_Owner;
            if (EntityManager.HasComponent<Waypoint>(target))
                info.WaypointIndex = EntityManager.GetComponentData<Waypoint>(target).m_Index;
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
            if (_routeRoot != null)
                _routeRoot.SetActive(_routeVisible);
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

        private void RenderRoute(DynamicBuffer<PathElement> path)
        {
            DestroyRouteOverlay();
            EnsureRouteMaterial();
            if (_routeMaterial == null)
            {
                _statusBinding.Update("Journey calculated, but no compatible Unity line shader was found for visualization.");
                return;
            }

            _routeRoot = new GameObject("JourneyPlanner.NativeRoute");
            _routePieces.Clear();
            _followPieceIndex = 0;
            _followFrame = 0;

            int walkCurves = 0;
            int transitCurves = 0;
            List<JourneyLeg> legs = BuildLegs(path);

            var discovery = new StringBuilder();

            // Render legs in actual journey order. This is important for citizen-follow
            // mode because route pieces can then be consumed sequentially behind A.
            foreach (JourneyLeg leg in legs)
            {
                if (leg.Mode == "Walk" || IsPrivateMode(leg.Mode))
                {
                    float width = leg.Mode == "Walk" ? 2.2f : 3.4f;
                    for (int i = leg.StartPathIndex; i <= leg.EndPathIndex && i < path.Length; i++)
                    {
                        if (TryGetCurvePoints(path[i].m_Target, out Vector3[] points))
                        {
                            CreateLine(leg.Mode + ".PathElement." + i, points, width, GetModeColor(leg.Mode), leg.StartPathIndex, leg.EndPathIndex);
                            walkCurves++;
                        }
                    }
                }
                else
                {
                    transitCurves += RenderTransitLegFromRouteSegments(leg, discovery);
                }
            }

            _routeRoot.SetActive(_routeVisible);
            if (_followCitizen != Entity.Null && EntityManager.Exists(_followCitizen))
                _statusBinding.Update("Journey ready. Route progress will follow the selected citizen.");
            else
                _statusBinding.Update("Journey ready.");
        }

        private int RenderTransitLegFromRouteSegments(JourneyLeg leg, StringBuilder log)
        {
            log.AppendLine();
            log.AppendLine(leg.Mode.ToUpperInvariant() + " " + (leg.RouteNumber >= 0 ? leg.RouteNumber.ToString(CultureInfo.InvariantCulture) : "?") +
                           " route=" + FormatEntity(leg.RouteOwner) + " waypoint " + leg.FirstWaypoint + " -> " + leg.LastWaypoint);

            if (leg.RouteOwner == Entity.Null || !EntityManager.Exists(leg.RouteOwner))
            {
                log.AppendLine("  Route owner missing.");
                return 0;
            }
            if (!EntityManager.HasBuffer<RouteSegment>(leg.RouteOwner) || !EntityManager.HasBuffer<RouteWaypoint>(leg.RouteOwner))
            {
                log.AppendLine("  RouteSegment/RouteWaypoint buffer missing.");
                return 0;
            }

            DynamicBuffer<RouteSegment> segments = EntityManager.GetBuffer<RouteSegment>(leg.RouteOwner, true);
            DynamicBuffer<RouteWaypoint> waypoints = EntityManager.GetBuffer<RouteWaypoint>(leg.RouteOwner, true);
            log.AppendLine("  RouteWaypoint.Length=" + waypoints.Length + " RouteSegment.Length=" + segments.Length);
            if (segments.Length == 0 || waypoints.Length == 0 || leg.FirstWaypoint < 0 || leg.LastWaypoint < 0)
                return 0;

            int current = leg.FirstWaypoint % segments.Length;
            if (current < 0) current += segments.Length;
            int safety = 0;
            int rendered = 0;
            while (safety++ < segments.Length + 1)
            {
                Entity segment = segments[current].m_Segment;
                log.AppendLine("  segment[" + current + "]=" + DescribeEntity(segment));
                rendered += RenderRouteSegmentPath(segment, leg.Mode, log, current, leg.StartPathIndex, leg.EndPathIndex);

                int nextWaypoint = (current + 1) % waypoints.Length;
                if (nextWaypoint == leg.LastWaypoint)
                    break;
                current = (current + 1) % segments.Length;
            }
            log.AppendLine("  rendered lane curves=" + rendered);
            return rendered;
        }

        private int RenderRouteSegmentPath(Entity segment, string mode, StringBuilder log, int segmentIndex, int journeyStartIndex, int journeyEndIndex)
        {
            if (segment == Entity.Null || !EntityManager.Exists(segment)) return 0;
            int rendered = 0;

            if (TryGetCurvePoints(segment, out Vector3[] ownCurve))
            {
                CreateLine("Transit." + mode + ".Segment." + segmentIndex, ownCurve, 4.0f, GetModeColor(mode), journeyStartIndex, journeyEndIndex);
                rendered++;
            }

            if (EntityManager.HasBuffer<PathElement>(segment))
            {
                DynamicBuffer<PathElement> path = EntityManager.GetBuffer<PathElement>(segment, true);
                log.AppendLine("    PathElement.Length=" + path.Length);
                for (int i = 0; i < path.Length; i++)
                {
                    Entity target = path[i].m_Target;
                    log.AppendLine("      [" + i + "] " + DescribeEntity(target) + " delta=" + path[i].m_TargetDelta);
                    if (TryGetCurvePoints(target, out Vector3[] points))
                    {
                        CreateLine("Transit." + mode + "." + segmentIndex + ".Path." + i, points, 4.0f, GetModeColor(mode), journeyStartIndex, journeyEndIndex);
                        rendered++;
                    }
                }
            }
            else
            {
                log.AppendLine("    PathElement buffer absent.");
            }

            if (EntityManager.HasBuffer<CurveSource>(segment))
                log.AppendLine("    CurveSource.Length=" + EntityManager.GetBuffer<CurveSource>(segment, true).Length);
            if (EntityManager.HasBuffer<CurveElement>(segment))
                log.AppendLine("    CurveElement.Length=" + EntityManager.GetBuffer<CurveElement>(segment, true).Length);

            return rendered;
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

                if (transit.IsTransit)
                {
                    privateMode = null;
                    mode = transit.Mode;
                    routeOwner = transit.RouteOwner;
                    routeNumber = transit.RouteNumber;
                    waypoint = transit.WaypointIndex;
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
                        RouteNumber = routeNumber, FirstWaypoint = waypoint, LastWaypoint = waypoint
                    };
                    legs.Add(current);
                }
                else
                {
                    current.EndPathIndex = i;
                    current.LastTarget = target;
                    if (waypoint >= 0) current.LastWaypoint = waypoint;
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

        private void EnsureRouteMaterial()
        {
            if (_routeMaterial != null) return;
            string[] names = { "HDRP/Unlit", "Shader Graphs/Unlit", "Universal Render Pipeline/Unlit", "Sprites/Default", "Unlit/Color" };
            foreach (string name in names)
            {
                Shader shader = Shader.Find(name);
                if (shader == null) continue;
                _routeMaterial = new Material(shader);
                _routeMaterial.name = "JourneyPlanner.NativeRouteMaterial";
                break;
            }
        }

        private void CreateLine(string name, Vector3[] points, float width, UnityEngine.Color color, int journeyStartIndex, int journeyEndIndex)
        {
            if (points == null || points.Length < 2 || _routeRoot == null) return;
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(_routeRoot.transform, false);
            LineRenderer line = obj.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.positionCount = points.Length;
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 4;
            line.numCornerVertices = 3;
            line.receiveShadows = false;
            line.material = new Material(_routeMaterial);
            line.startColor = color;
            line.endColor = color;
            line.material.color = color;
            if (line.material.HasProperty("_BaseColor")) line.material.SetColor("_BaseColor", color);
            if (line.material.HasProperty("_EmissiveColor")) line.material.SetColor("_EmissiveColor", color * 1.5f);
            line.SetPositions(points);

            _routePieces.Add(new RoutePiece
            {
                Object = obj,
                Line = line,
                OriginalPoints = (Vector3[])points.Clone(),
                JourneyStartIndex = journeyStartIndex,
                JourneyEndIndex = journeyEndIndex
            });
        }

        private UnityEngine.Color GetModeColor(string mode)
        {
            switch ((mode ?? "").ToLowerInvariant())
            {
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

        private bool TryGetCurvePoints(Entity entity, out Vector3[] points)
        {
            points = null;
            if (entity == Entity.Null || !EntityManager.Exists(entity) || !EntityManager.HasComponent<Game.Net.Curve>(entity))
                return false;
            try
            {
                Game.Net.Curve curve = EntityManager.GetComponentData<Game.Net.Curve>(entity);
                object boxed = curve;
                FieldInfo bezierField = boxed.GetType().GetField("m_Bezier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (bezierField == null) return false;
                object bezier = bezierField.GetValue(boxed);
                if (!TryReadFloat3(bezier, "a", "m_A", out float3 a) ||
                    !TryReadFloat3(bezier, "b", "m_B", out float3 b) ||
                    !TryReadFloat3(bezier, "c", "m_C", out float3 c) ||
                    !TryReadFloat3(bezier, "d", "m_D", out float3 d)) return false;
                points = new Vector3[CurveSamples + 1];
                for (int i = 0; i <= CurveSamples; i++)
                {
                    float t = i / (float)CurveSamples;
                    float u = 1f - t;
                    float3 pos = u*u*u*a + 3f*u*u*t*b + 3f*u*t*t*c + t*t*t*d;
                    points[i] = new Vector3(pos.x, pos.y + RouteHeightOffset, pos.z);
                }
                return true;
            }
            catch { return false; }
        }

        private static bool TryReadFloat3(object obj, string field1, string field2, out float3 value)
        {
            value = float3.zero;
            if (obj == null) return false;
            FieldInfo f = obj.GetType().GetField(field1, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                          obj.GetType().GetField(field2, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f == null) return false;
            object raw = f.GetValue(obj);
            if (raw is float3 p) { value = p; return true; }
            return false;
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
            if (_followCitizen == Entity.Null || _routeRoot == null || _routePieces.Count == 0 || !_routeVisible)
                return;
            if (!EntityManager.Exists(_followCitizen))
            {
                _followCitizen = Entity.Null;
                return;
            }
            if (++_followFrame < FollowUpdateInterval) return;
            _followFrame = 0;

            if (!TryGetFollowerPosition(_followCitizen, out float3 world)) return;
            Vector3 follower = new Vector3(world.x, world.y + RouteHeightOffset, world.z);

            int bestPiece = -1;
            int bestPoint = -1;
            float bestDistanceSq = FollowAcquireDistance * FollowAcquireDistance;

            // Progress is monotonic. Search forward from the current piece so a route
            // crossing cannot make the line jump backwards.
            for (int p = _followPieceIndex; p < _routePieces.Count; p++)
            {
                RoutePiece piece = _routePieces[p];
                Vector3[] pts = piece.OriginalPoints;
                for (int i = 0; i < pts.Length; i++)
                {
                    float dx = pts[i].x - follower.x;
                    float dz = pts[i].z - follower.z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 < bestDistanceSq)
                    {
                        bestDistanceSq = d2;
                        bestPiece = p;
                        bestPoint = i;
                    }
                }

                // Prefer the earliest route piece once we have a close hit. This
                // prevents nearby parallel bus/rail lanes from skipping far ahead.
                if (bestPiece == p && bestDistanceSq < 18f * 18f)
                    break;
            }

            if (bestPiece < 0) return; // Off the proposed route: do not fake progress.

            for (int p = _followPieceIndex; p < bestPiece; p++)
                if (_routePieces[p].Object != null) _routePieces[p].Object.SetActive(false);

            _followPieceIndex = Math.Max(_followPieceIndex, bestPiece);
            RoutePiece current = _routePieces[_followPieceIndex];
            if (current.Object == null || current.Line == null || bestPoint < 0) return;
            current.Object.SetActive(true);

            // Keep the exact nearest point as the new beginning, then all remaining
            // original samples. The line therefore visually ends at the citizen and
            // disappears behind them as they/vehicle advance.
            int remaining = current.OriginalPoints.Length - bestPoint;
            if (remaining < 2)
            {
                current.Object.SetActive(false);
                if (_followPieceIndex + 1 < _routePieces.Count) _followPieceIndex++;
                return;
            }
            Vector3[] tail = new Vector3[remaining];
            Array.Copy(current.OriginalPoints, bestPoint, tail, 0, remaining);
            tail[0] = follower;
            current.Line.positionCount = tail.Length;
            current.Line.SetPositions(tail);
        }

        private bool TryGetFollowerPosition(Entity citizen, out float3 position)
        {
            position = float3.zero;

            // While riding public transport, follow the vehicle rather than the
            // passenger entity. Reflection avoids depending on a specific field name
            // across CS2 builds while still using the native CurrentVehicle component.
            if (EntityManager.HasComponent<CurrentVehicle>(citizen))
            {
                try
                {
                    object boxed = EntityManager.GetComponentData<CurrentVehicle>(citizen);
                    FieldInfo f = boxed.GetType().GetField("m_Vehicle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f != null && f.GetValue(boxed) is Entity vehicle && vehicle != Entity.Null && EntityManager.Exists(vehicle))
                    {
                        if (TryGetWorldPosition(vehicle, out position)) return true;
                    }
                }
                catch { }
            }

            return TryGetWorldPosition(citizen, out position);
        }

        private void DestroyRouteOverlay()
        {
            if (_routeRoot != null)
            {
                UnityEngine.Object.Destroy(_routeRoot);
                _routeRoot = null;
            }
            _routePieces.Clear();
            _followPieceIndex = 0;
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
            if (_routeMaterial != null)
            {
                UnityEngine.Object.Destroy(_routeMaterial);
                _routeMaterial = null;
            }
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
            public GameObject Object;
            public LineRenderer Line;
            public Vector3[] OriginalPoints;
            public int JourneyStartIndex;
            public int JourneyEndIndex;
        }

        private sealed class JourneyLeg
        {
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
        }
    }
}
