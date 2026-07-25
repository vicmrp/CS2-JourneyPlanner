using Colossal.UI.Binding;
using Game.Tools;
using Game.UI;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2_JourneyPlanner
{
    public enum SelectionMode
    {
        None,
        Start,
        Destination
    }

    public sealed partial class JourneyPlannerUISystem : UISystemBase
    {
        private const string BindingGroup = "JourneyPlanner";

        private ValueBinding<string> _selectionModeBinding;
        private ValueBinding<bool> _hasStartBinding;
        private ValueBinding<bool> _hasDestinationBinding;
        private ValueBinding<string> _statusBinding;
        private ValueBinding<string> _startPositionBinding;
        private ValueBinding<string> _destinationPositionBinding;

        private ToolSystem _toolSystem;
        private JourneyPlannerToolSystem _journeyToolSystem;

        public SelectionMode CurrentSelectionMode { get; private set; }

        public bool HasStart { get; private set; }

        public bool HasDestination { get; private set; }

        public Entity StartOwner { get; private set; }

        public Entity DestinationOwner { get; private set; }

        public float3 StartPosition { get; private set; }

        public float3 DestinationPosition { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();

            Mod.Log.Info("JourneyPlannerUISystem.OnCreate.");

            _toolSystem =
                World.GetOrCreateSystemManaged<ToolSystem>();

            _journeyToolSystem =
                World.GetOrCreateSystemManaged<JourneyPlannerToolSystem>();

            _selectionModeBinding = new ValueBinding<string>(
                BindingGroup,
                "SelectionMode",
                "none"
            );

            _hasStartBinding = new ValueBinding<bool>(
                BindingGroup,
                "HasStart",
                false
            );

            _hasDestinationBinding = new ValueBinding<bool>(
                BindingGroup,
                "HasDestination",
                false
            );

            _statusBinding = new ValueBinding<string>(
                BindingGroup,
                "Status",
                "Select a starting point"
            );

            _startPositionBinding = new ValueBinding<string>(
                BindingGroup,
                "StartPosition",
                ""
            );

            _destinationPositionBinding = new ValueBinding<string>(
                BindingGroup,
                "DestinationPosition",
                ""
            );

            AddBinding(_selectionModeBinding);
            AddBinding(_hasStartBinding);
            AddBinding(_hasDestinationBinding);
            AddBinding(_statusBinding);
            AddBinding(_startPositionBinding);
            AddBinding(_destinationPositionBinding);

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
                    "ClearRoute",
                    ClearRoute
                )
            );

            AddBinding(
                new TriggerBinding(
                    BindingGroup,
                    "CalculateRoute",
                    CalculateRoute
                )
            );

            Mod.Log.Info("Journey Planner UI bindings added.");
        }

        private void SelectStart()
        {
            Mod.Log.Info("SelectStart trigger received.");

            SetSelectionMode(SelectionMode.Start);

            _statusBinding.Update(
                "Click on the map to select the starting point"
            );

            ActivateJourneyTool();
        }

        private void SelectDestination()
        {
            Mod.Log.Info("SelectDestination trigger received.");

            SetSelectionMode(SelectionMode.Destination);

            _statusBinding.Update(
                "Click on the map to select the destination"
            );

            ActivateJourneyTool();
        }

        private void ActivateJourneyTool()
        {
            if (_toolSystem.activeTool == _journeyToolSystem)
            {
                Mod.Log.Info("Journey Planner tool is already active.");
                return;
            }

            Mod.Log.Info("Activating Journey Planner tool.");

            _toolSystem.activeTool = _journeyToolSystem;
        }

        public void ConfirmSelection(
            Entity owner,
            float3 position
        )
        {
            string formattedPosition = FormatPosition(position);

            switch (CurrentSelectionMode)
            {
                case SelectionMode.Start:
                    StartOwner = owner;
                    StartPosition = position;
                    HasStart = true;

                    _hasStartBinding.Update(true);
                    _startPositionBinding.Update(formattedPosition);
                    _statusBinding.Update("Starting point selected");

                    Mod.Log.Info(
                        $"Start selected. Owner={owner}, " +
                        $"Position={formattedPosition}"
                    );
                    break;

                case SelectionMode.Destination:
                    DestinationOwner = owner;
                    DestinationPosition = position;
                    HasDestination = true;

                    _hasDestinationBinding.Update(true);
                    _destinationPositionBinding.Update(
                        formattedPosition
                    );

                    _statusBinding.Update("Destination selected");

                    Mod.Log.Info(
                        $"Destination selected. Owner={owner}, " +
                        $"Position={formattedPosition}"
                    );
                    break;

                default:
                    Mod.Log.Warn(
                        "ConfirmSelection called without an active mode."
                    );
                    return;
            }

            SetSelectionMode(SelectionMode.None);

            _journeyToolSystem.ReturnToDefaultTool();
        }

        public void CancelSelection()
        {
            Mod.Log.Info("Map selection cancelled.");

            SetSelectionMode(SelectionMode.None);
            _statusBinding.Update("Selection cancelled");
        }

        private void ClearRoute()
        {
            Mod.Log.Info("ClearRoute trigger received.");

            HasStart = false;
            HasDestination = false;

            StartOwner = Entity.Null;
            DestinationOwner = Entity.Null;

            StartPosition = default;
            DestinationPosition = default;

            _hasStartBinding.Update(false);
            _hasDestinationBinding.Update(false);

            _startPositionBinding.Update("");
            _destinationPositionBinding.Update("");

            SetSelectionMode(SelectionMode.None);

            _statusBinding.Update("Journey points cleared");

            _journeyToolSystem.ReturnToDefaultTool();

            Mod.Log.Info("Journey points cleared.");
        }

        private void CalculateRoute()
        {
            Mod.Log.Info(
                $"CalculateRoute requested. " +
                $"Start={HasStart}, Destination={HasDestination}"
            );

            if (!HasStart || !HasDestination)
            {
                _statusBinding.Update(
                    "Select both points before calculating"
                );

                return;
            }

            _statusBinding.Update(
                "Both positions are stored. Pathfinding is not implemented yet."
            );

            Mod.Log.Info(
                $"Ready for pathfinding. " +
                $"Start={FormatPosition(StartPosition)}, " +
                $"Destination={FormatPosition(DestinationPosition)}"
            );
        }

        private void SetSelectionMode(SelectionMode mode)
        {
            CurrentSelectionMode = mode;

            string value;

            switch (mode)
            {
                case SelectionMode.Start:
                    value = "start";
                    break;

                case SelectionMode.Destination:
                    value = "destination";
                    break;

                default:
                    value = "none";
                    break;
            }

            _selectionModeBinding.Update(value);
        }

        private static string FormatPosition(float3 position)
        {
            return
                $"X: {position.x:F1}, " +
                $"Y: {position.y:F1}, " +
                $"Z: {position.z:F1}";
        }
    }
}