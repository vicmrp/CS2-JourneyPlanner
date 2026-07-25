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

        private ValueBinding<string> _startEntityTypeBinding;
        private ValueBinding<string> _destinationEntityTypeBinding;

        private ToolSystem _toolSystem;
        private JourneyPlannerToolSystem _journeyToolSystem;

        public SelectionMode CurrentSelectionMode { get; private set; }

        public bool HasStart { get; private set; }

        public bool HasDestination { get; private set; }

        public Entity StartOwner { get; private set; }

        public Entity DestinationOwner { get; private set; }

        public float3 StartPosition { get; private set; }

        public float3 DestinationPosition { get; private set; }

        public string StartEntityType { get; private set; }

        public string DestinationEntityType { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();

            Mod.Log.Info("JourneyPlannerUISystem.OnCreate.");

            _toolSystem =
                World.GetOrCreateSystemManaged<ToolSystem>();

            _journeyToolSystem =
                World.GetOrCreateSystemManaged<JourneyPlannerToolSystem>();

            CurrentSelectionMode = SelectionMode.None;

            StartOwner = Entity.Null;
            DestinationOwner = Entity.Null;

            StartEntityType = string.Empty;
            DestinationEntityType = string.Empty;

            CreateBindings();
            RegisterTriggers();

            Mod.Log.Info("Journey Planner UI bindings added.");
        }

        private void CreateBindings()
        {
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
                string.Empty
            );

            _destinationPositionBinding = new ValueBinding<string>(
                BindingGroup,
                "DestinationPosition",
                string.Empty
            );

            _startEntityTypeBinding = new ValueBinding<string>(
                BindingGroup,
                "StartEntityType",
                string.Empty
            );

            _destinationEntityTypeBinding = new ValueBinding<string>(
                BindingGroup,
                "DestinationEntityType",
                string.Empty
            );

            AddBinding(_selectionModeBinding);
            AddBinding(_hasStartBinding);
            AddBinding(_hasDestinationBinding);
            AddBinding(_statusBinding);
            AddBinding(_startPositionBinding);
            AddBinding(_destinationPositionBinding);
            AddBinding(_startEntityTypeBinding);
            AddBinding(_destinationEntityTypeBinding);
        }

        private void RegisterTriggers()
        {
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
        }

        private void SelectStart()
        {
            Mod.Log.Info("SelectStart trigger received.");

            BeginSelection(
                SelectionMode.Start,
                "Click on the map to select the starting point"
            );
        }

        private void SelectDestination()
        {
            Mod.Log.Info("SelectDestination trigger received.");

            BeginSelection(
                SelectionMode.Destination,
                "Click on the map to select the destination"
            );
        }

        private void BeginSelection(
            SelectionMode mode,
            string status
        )
        {
            SetSelectionMode(mode);

            _statusBinding.Update(status);

            ActivateJourneyTool();
        }

        private void ActivateJourneyTool()
        {
            if (_toolSystem.activeTool == _journeyToolSystem)
            {
                Mod.Log.Info(
                    "Journey Planner tool is already active."
                );

                return;
            }

            Mod.Log.Info("Activating Journey Planner tool.");

            _toolSystem.activeTool = _journeyToolSystem;
        }

        public void ConfirmSelection(
            Entity owner,
            float3 position,
            string entityType
        )
        {
            if (CurrentSelectionMode == SelectionMode.None)
            {
                Mod.Log.Warn(
                    "ConfirmSelection called without an active selection mode."
                );

                return;
            }

            string formattedPosition =
                FormatPosition(position);

            switch (CurrentSelectionMode)
            {
                case SelectionMode.Start:
                    StoreStartPoint(
                        owner,
                        position,
                        formattedPosition,
                        entityType
                    );
                    break;

                case SelectionMode.Destination:
                    StoreDestinationPoint(
                        owner,
                        position,
                        formattedPosition,
                        entityType
                    );
                    break;
            }

            SetSelectionMode(SelectionMode.None);
            UpdateReadyStatus();

            _journeyToolSystem.ReturnToDefaultTool();
        }

        public void RejectSelection(string reason)
        {
            Mod.Log.Warn(
                $"Journey point rejected: {reason}"
            );

            _statusBinding.Update(
                $"Invalid point: {reason}"
            );
        }

        private void StoreStartPoint(
            Entity owner,
            float3 position,
            string formattedPosition,
            string entityType
        )
        {
            StartOwner = owner;
            StartPosition = position;
            StartEntityType = entityType;
            HasStart = true;

            _hasStartBinding.Update(true);
            _startPositionBinding.Update(formattedPosition);
            _startEntityTypeBinding.Update(entityType);

            Mod.Log.Info(
                $"Start selected. " +
                $"Owner={owner}, " +
                $"Type={entityType}, " +
                $"Position={formattedPosition}"
            );
        }

        private void StoreDestinationPoint(
            Entity owner,
            float3 position,
            string formattedPosition,
            string entityType
        )
        {
            DestinationOwner = owner;
            DestinationPosition = position;
            DestinationEntityType = entityType;
            HasDestination = true;

            _hasDestinationBinding.Update(true);

            _destinationPositionBinding.Update(
                formattedPosition
            );

            _destinationEntityTypeBinding.Update(
                entityType
            );

            Mod.Log.Info(
                $"Destination selected. " +
                $"Owner={owner}, " +
                $"Type={entityType}, " +
                $"Position={formattedPosition}"
            );
        }

        public void CancelSelection()
        {
            Mod.Log.Info("Map selection cancelled.");

            SetSelectionMode(SelectionMode.None);
            UpdateReadyStatus();
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

            StartEntityType = string.Empty;
            DestinationEntityType = string.Empty;

            _hasStartBinding.Update(false);
            _hasDestinationBinding.Update(false);

            _startPositionBinding.Update(string.Empty);
            _destinationPositionBinding.Update(string.Empty);

            _startEntityTypeBinding.Update(string.Empty);
            _destinationEntityTypeBinding.Update(string.Empty);

            SetSelectionMode(SelectionMode.None);

            _statusBinding.Update(
                "Journey points cleared"
            );

            _journeyToolSystem.ReturnToDefaultTool();

            Mod.Log.Info("Journey points cleared.");
        }

        private void CalculateRoute()
        {
            Mod.Log.Info(
                $"CalculateRoute requested. " +
                $"Start={HasStart}, " +
                $"Destination={HasDestination}"
            );

            if (!HasStart || !HasDestination)
            {
                _statusBinding.Update(
                    "Select both points before calculating"
                );

                return;
            }

            _statusBinding.Update(
                "Both positions are stored. " +
                "Network snapping is not implemented yet."
            );

            Mod.Log.Info(
                $"Ready for network snapping. " +
                $"Start={FormatPosition(StartPosition)}, " +
                $"StartType={StartEntityType}, " +
                $"Destination={FormatPosition(DestinationPosition)}, " +
                $"DestinationType={DestinationEntityType}"
            );
        }

        private void UpdateReadyStatus()
        {
            if (HasStart && HasDestination)
            {
                _statusBinding.Update(
                    "Both points selected. Ready to calculate route."
                );

                return;
            }

            if (HasStart)
            {
                _statusBinding.Update(
                    "Starting point selected. Select a destination."
                );

                return;
            }

            if (HasDestination)
            {
                _statusBinding.Update(
                    "Destination selected. Select a starting point."
                );

                return;
            }

            _statusBinding.Update(
                "Select a starting point"
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

        private static string FormatPosition(
            float3 position
        )
        {
            return
                $"X: {position.x:F1}, " +
                $"Y: {position.y:F1}, " +
                $"Z: {position.z:F1}";
        }
    }
}