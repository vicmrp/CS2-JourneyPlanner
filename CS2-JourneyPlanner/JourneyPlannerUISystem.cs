using Colossal.UI.Binding;
using Game.Tools;
using Game.UI;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2_JourneyPlanner
{
    public sealed partial class JourneyPlannerUISystem
        : UISystemBase
    {
        private const string BindingGroup =
            "JourneyPlanner";

        private JourneyPlannerToolSystem _toolSystem;
        private ToolSystem _gameToolSystem;

        private bool _windowVisible;

        private ValueBinding<bool>
            _windowVisibleBinding;

        private ValueBinding<string>
            _selectionModeBinding;

        private ValueBinding<bool>
            _hasStartBinding;

        private ValueBinding<bool>
            _hasDestinationBinding;

        private ValueBinding<string>
            _statusBinding;

        private ValueBinding<string>
            _startPositionBinding;

        private ValueBinding<string>
            _destinationPositionBinding;

        private ValueBinding<string>
            _startEntityTypeBinding;

        private ValueBinding<string>
            _destinationEntityTypeBinding;

        private ValueBinding<string>
            _startRoadNameBinding;

        private ValueBinding<string>
            _destinationRoadNameBinding;

        public Entity StartOwner { get; private set; }

        public Entity DestinationOwner { get; private set; }

        public Entity StartAggregate { get; private set; }

        public Entity DestinationAggregate { get; private set; }

        public float3 StartPosition { get; private set; }

        public float3 DestinationPosition { get; private set; }

        public string StartRoadName { get; private set; }

        public string DestinationRoadName { get; private set; }

        public SelectionMode CurrentSelectionMode
        {
            get;
            private set;
        }

        public bool HasStart =>
            StartOwner != Entity.Null;

        public bool HasDestination =>
            DestinationOwner != Entity.Null;

        protected override void OnCreate()
        {
            base.OnCreate();

            Mod.Log.Info(
                "JourneyPlannerUISystem.OnCreate."
            );

            _toolSystem =
                World.GetOrCreateSystemManaged<
                    JourneyPlannerToolSystem
                >();

            _gameToolSystem =
                World.GetOrCreateSystemManaged<
                    ToolSystem
                >();

            /*
             * The panel starts closed.
             * The launcher button remains visible.
             */
            _windowVisible = false;

            StartOwner = Entity.Null;
            DestinationOwner = Entity.Null;

            StartAggregate = Entity.Null;
            DestinationAggregate = Entity.Null;

            StartPosition = float3.zero;
            DestinationPosition = float3.zero;

            StartRoadName = string.Empty;
            DestinationRoadName = string.Empty;

            CurrentSelectionMode =
                SelectionMode.None;

            CreateBindings();
        }

        private void CreateBindings()
        {
            _windowVisibleBinding =
                new ValueBinding<bool>(
                    BindingGroup,
                    "WindowVisible",
                    _windowVisible
                );

            _selectionModeBinding =
                new ValueBinding<string>(
                    BindingGroup,
                    "SelectionMode",
                    SelectionMode.None.ToString()
                );

            _hasStartBinding =
                new ValueBinding<bool>(
                    BindingGroup,
                    "HasStart",
                    false
                );

            _hasDestinationBinding =
                new ValueBinding<bool>(
                    BindingGroup,
                    "HasDestination",
                    false
                );

            _statusBinding =
                new ValueBinding<string>(
                    BindingGroup,
                    "Status",
                    "Select a starting point."
                );

            _startPositionBinding =
                new ValueBinding<string>(
                    BindingGroup,
                    "StartPosition",
                    string.Empty
                );

            _destinationPositionBinding =
                new ValueBinding<string>(
                    BindingGroup,
                    "DestinationPosition",
                    string.Empty
                );

            _startEntityTypeBinding =
                new ValueBinding<string>(
                    BindingGroup,
                    "StartEntityType",
                    string.Empty
                );

            _destinationEntityTypeBinding =
                new ValueBinding<string>(
                    BindingGroup,
                    "DestinationEntityType",
                    string.Empty
                );

            _startRoadNameBinding =
                new ValueBinding<string>(
                    BindingGroup,
                    "StartRoadName",
                    string.Empty
                );

            _destinationRoadNameBinding =
                new ValueBinding<string>(
                    BindingGroup,
                    "DestinationRoadName",
                    string.Empty
                );

            AddBinding(_windowVisibleBinding);

            AddBinding(_selectionModeBinding);
            AddBinding(_hasStartBinding);
            AddBinding(_hasDestinationBinding);
            AddBinding(_statusBinding);

            AddBinding(_startPositionBinding);
            AddBinding(_destinationPositionBinding);

            AddBinding(_startEntityTypeBinding);
            AddBinding(_destinationEntityTypeBinding);

            AddBinding(_startRoadNameBinding);
            AddBinding(_destinationRoadNameBinding);

            AddBinding(
                new TriggerBinding(
                    BindingGroup,
                    "OpenWindow",
                    OpenWindow
                )
            );

            AddBinding(
                new TriggerBinding(
                    BindingGroup,
                    "CloseWindow",
                    CloseWindow
                )
            );

            AddBinding(
                new TriggerBinding(
                    BindingGroup,
                    "ToggleWindow",
                    ToggleWindow
                )
            );

            AddBinding(
                new TriggerBinding(
                    BindingGroup,
                    "SelectStart",
                    SelectStart
                )
            );

            AddBinding(
                new TriggerBinding(
                    BindingGroup,
                    "SelectDestination",
                    SelectDestination
                )
            );

            AddBinding(
                new TriggerBinding(
                    BindingGroup,
                    "ClearStart",
                    ClearStart
                )
            );

            AddBinding(
                new TriggerBinding(
                    BindingGroup,
                    "ClearDestination",
                    ClearDestination
                )
            );

            AddBinding(
                new TriggerBinding(
                    BindingGroup,
                    "ClearAll",
                    ClearAll
                )
            );

            AddBinding(
                new TriggerBinding(
                    BindingGroup,
                    "CalculateRoute",
                    CalculateRoute
                )
            );

            Mod.Log.Info(
                "Journey Planner UI bindings added."
            );
        }

        private void OpenWindow()
        {
            if (_windowVisible)
            {
                return;
            }

            _windowVisible = true;

            _windowVisibleBinding.Update(true);

            Mod.Log.Info(
                "Journey Planner window opened."
            );
        }

        private void CloseWindow()
        {
            if (!_windowVisible)
            {
                return;
            }

            /*
             * Stop map selection before hiding the panel.
             */
            CancelActiveSelection();

            _windowVisible = false;

            _windowVisibleBinding.Update(false);

            Mod.Log.Info(
                "Journey Planner window closed."
            );
        }

        private void ToggleWindow()
        {
            if (_windowVisible)
            {
                CloseWindow();
            }
            else
            {
                OpenWindow();
            }
        }

        private void SelectStart()
        {
            OpenWindow();

            Mod.Log.Info(
                "SelectStart trigger received."
            );

            CurrentSelectionMode =
                SelectionMode.Start;

            _selectionModeBinding.Update(
                CurrentSelectionMode.ToString()
            );

            _statusBinding.Update(
                "Click a road to select the starting point."
            );

            Mod.Log.Info(
                "Selection mode changed to Start."
            );

            ActivateJourneyPlannerTool();
        }

        private void SelectDestination()
        {
            OpenWindow();

            Mod.Log.Info(
                "SelectDestination trigger received."
            );

            CurrentSelectionMode =
                SelectionMode.Destination;

            _selectionModeBinding.Update(
                CurrentSelectionMode.ToString()
            );

            _statusBinding.Update(
                "Click a road to select the destination."
            );

            Mod.Log.Info(
                "Selection mode changed to Destination."
            );

            ActivateJourneyPlannerTool();
        }

        private void ActivateJourneyPlannerTool()
        {
            Mod.Log.Info(
                "Activating Journey Planner tool."
            );

            _gameToolSystem.activeTool =
                _toolSystem;
        }

        public void ConfirmRoadSelection(
            Entity roadEdge,
            Entity aggregateEntity,
            float3 position,
            string roadName
        )
        {
            string safeRoadName =
                string.IsNullOrWhiteSpace(roadName)
                    ? "Unnamed road"
                    : roadName;

            string formattedPosition =
                FormatPosition(position);

            string formattedEntity =
                roadEdge.ToString();

            switch (CurrentSelectionMode)
            {
                case SelectionMode.Start:
                    StartOwner = roadEdge;
                    StartAggregate = aggregateEntity;
                    StartPosition = position;
                    StartRoadName = safeRoadName;

                    _hasStartBinding.Update(true);

                    _startRoadNameBinding.Update(
                        StartRoadName
                    );

                    _startPositionBinding.Update(
                        formattedPosition
                    );

                    _startEntityTypeBinding.Update(
                        formattedEntity
                    );

                    Mod.Log.Info(
                        $"Start road selected. " +
                        $"Name={StartRoadName}, " +
                        $"Entity={StartOwner}, " +
                        $"Aggregate={StartAggregate}, " +
                        $"Position={formattedPosition}"
                    );

                    break;

                case SelectionMode.Destination:
                    DestinationOwner = roadEdge;
                    DestinationAggregate =
                        aggregateEntity;

                    DestinationPosition = position;
                    DestinationRoadName =
                        safeRoadName;

                    _hasDestinationBinding.Update(
                        true
                    );

                    _destinationRoadNameBinding.Update(
                        DestinationRoadName
                    );

                    _destinationPositionBinding.Update(
                        formattedPosition
                    );

                    _destinationEntityTypeBinding.Update(
                        formattedEntity
                    );

                    Mod.Log.Info(
                        $"Destination road selected. " +
                        $"Name={DestinationRoadName}, " +
                        $"Entity={DestinationOwner}, " +
                        $"Aggregate={DestinationAggregate}, " +
                        $"Position={formattedPosition}"
                    );

                    break;

                default:
                    Mod.Log.Warn(
                        "A road was selected while no " +
                        "selection mode was active."
                    );

                    _statusBinding.Update(
                        "No selection mode was active."
                    );

                    return;
            }

            CurrentSelectionMode =
                SelectionMode.None;

            _selectionModeBinding.Update(
                CurrentSelectionMode.ToString()
            );

            UpdateStatus();

            _toolSystem.ReturnToDefaultTool();
        }

        public void RejectSelection(
            string reason
        )
        {
            _statusBinding.Update(reason);
        }

        public void CancelSelection()
        {
            CurrentSelectionMode =
                SelectionMode.None;

            _selectionModeBinding.Update(
                CurrentSelectionMode.ToString()
            );

            UpdateStatus();

            Mod.Log.Info(
                "Journey Planner selection cancelled."
            );
        }

        private void CancelActiveSelection()
        {
            bool wasSelecting =
                CurrentSelectionMode !=
                SelectionMode.None;

            CurrentSelectionMode =
                SelectionMode.None;

            _selectionModeBinding.Update(
                CurrentSelectionMode.ToString()
            );

            if (_gameToolSystem.activeTool == _toolSystem)
            {
                _toolSystem.ReturnToDefaultTool();
            }

            if (wasSelecting)
            {
                UpdateStatus();

                Mod.Log.Info(
                    "Active Journey Planner selection " +
                    "cancelled because the window closed."
                );
            }
        }

        private void ClearStart()
        {
            StartOwner = Entity.Null;
            StartAggregate = Entity.Null;
            StartPosition = float3.zero;
            StartRoadName = string.Empty;

            _hasStartBinding.Update(false);

            _startRoadNameBinding.Update(
                string.Empty
            );

            _startPositionBinding.Update(
                string.Empty
            );

            _startEntityTypeBinding.Update(
                string.Empty
            );

            UpdateStatus();

            Mod.Log.Info(
                "Start selection cleared."
            );
        }

        private void ClearDestination()
        {
            DestinationOwner = Entity.Null;
            DestinationAggregate = Entity.Null;
            DestinationPosition = float3.zero;
            DestinationRoadName = string.Empty;

            _hasDestinationBinding.Update(false);

            _destinationRoadNameBinding.Update(
                string.Empty
            );

            _destinationPositionBinding.Update(
                string.Empty
            );

            _destinationEntityTypeBinding.Update(
                string.Empty
            );

            UpdateStatus();

            Mod.Log.Info(
                "Destination selection cleared."
            );
        }

        private void ClearAll()
        {
            CancelActiveSelection();

            StartOwner = Entity.Null;
            DestinationOwner = Entity.Null;

            StartAggregate = Entity.Null;
            DestinationAggregate = Entity.Null;

            StartPosition = float3.zero;
            DestinationPosition = float3.zero;

            StartRoadName = string.Empty;
            DestinationRoadName = string.Empty;

            _hasStartBinding.Update(false);
            _hasDestinationBinding.Update(false);

            _startRoadNameBinding.Update(
                string.Empty
            );

            _destinationRoadNameBinding.Update(
                string.Empty
            );

            _startPositionBinding.Update(
                string.Empty
            );

            _destinationPositionBinding.Update(
                string.Empty
            );

            _startEntityTypeBinding.Update(
                string.Empty
            );

            _destinationEntityTypeBinding.Update(
                string.Empty
            );

            _statusBinding.Update(
                "Select a starting point."
            );

            Mod.Log.Info(
                "All Journey Planner selections cleared."
            );
        }

        private void CalculateRoute()
        {
            if (!HasStart)
            {
                _statusBinding.Update(
                    "Select a starting point first."
                );

                return;
            }

            if (!HasDestination)
            {
                _statusBinding.Update(
                    "Select a destination first."
                );

                return;
            }

            Mod.Log.Info(
                $"Ready for pathfinding. " +
                $"StartEntity={StartOwner}, " +
                $"StartRoad={StartRoadName}, " +
                $"DestinationEntity={DestinationOwner}, " +
                $"DestinationRoad={DestinationRoadName}"
            );

            _statusBinding.Update(
                $"Ready to calculate a route from " +
                $"{StartRoadName} to " +
                $"{DestinationRoadName}."
            );
        }

        private void UpdateStatus()
        {
            if (HasStart && HasDestination)
            {
                _statusBinding.Update(
                    $"Ready to calculate a route from " +
                    $"{StartRoadName} to " +
                    $"{DestinationRoadName}."
                );

                return;
            }

            if (HasStart)
            {
                _statusBinding.Update(
                    $"Starting point: {StartRoadName}. " +
                    "Select a destination."
                );

                return;
            }

            if (HasDestination)
            {
                _statusBinding.Update(
                    $"Destination: {DestinationRoadName}. " +
                    "Select a starting point."
                );

                return;
            }

            _statusBinding.Update(
                "Select a starting point."
            );
        }

        private static string FormatPosition(
            float3 position
        )
        {
            return
                $"X: {position.x:F1}, " +
                $"Y: {position.y:F1}, " +
                $"Z: {position.z:F1}";
        }

        public enum SelectionMode
        {
            None,
            Start,
            Destination
        }
    }
}