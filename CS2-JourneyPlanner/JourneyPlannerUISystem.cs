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

    public sealed partial class JourneyPlannerUISystem
        : UISystemBase
    {
        private const string BindingGroup =
            "JourneyPlanner";

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

        private ToolSystem _toolSystem;

        private JourneyPlannerToolSystem
            _journeyToolSystem;

        public SelectionMode CurrentSelectionMode
        {
            get;
            private set;
        }

        public bool HasStart
        {
            get;
            private set;
        }

        public bool HasDestination
        {
            get;
            private set;
        }

        public Entity StartOwner
        {
            get;
            private set;
        }

        public Entity DestinationOwner
        {
            get;
            private set;
        }

        public float3 StartPosition
        {
            get;
            private set;
        }

        public float3 DestinationPosition
        {
            get;
            private set;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            Mod.Log.Info(
                "JourneyPlannerUISystem.OnCreate."
            );

            _toolSystem =
                World.GetOrCreateSystemManaged<
                    ToolSystem
                >();

            _journeyToolSystem =
                World.GetOrCreateSystemManaged<
                    JourneyPlannerToolSystem
                >();

            CurrentSelectionMode =
                SelectionMode.None;

            StartOwner = Entity.Null;
            DestinationOwner = Entity.Null;

            CreateValueBindings();
            CreateTriggerBindings();

            Mod.Log.Info(
                "Journey Planner UI bindings added."
            );
        }

        private void CreateValueBindings()
        {
            _selectionModeBinding =
                new ValueBinding<string>(
                    BindingGroup,
                    "SelectionMode",
                    "none"
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
                    "Select a starting road"
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

            AddBinding(_selectionModeBinding);
            AddBinding(_hasStartBinding);
            AddBinding(_hasDestinationBinding);
            AddBinding(_statusBinding);
            AddBinding(_startPositionBinding);
            AddBinding(_destinationPositionBinding);
            AddBinding(_startEntityTypeBinding);
            AddBinding(_destinationEntityTypeBinding);
        }

        private void CreateTriggerBindings()
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
            Mod.Log.Info(
                "SelectStart trigger received."
            );

            BeginSelection(
                SelectionMode.Start,
                "Click a road to select the starting point"
            );
        }

        private void SelectDestination()
        {
            Mod.Log.Info(
                "SelectDestination trigger received."
            );

            BeginSelection(
                SelectionMode.Destination,
                "Click a road to select the destination"
            );
        }

        private void BeginSelection(
            SelectionMode selectionMode,
            string status
        )
        {
            SetSelectionMode(selectionMode);
            _statusBinding.Update(status);

            Mod.Log.Info(
                $"Selection mode changed to " +
                $"{selectionMode}."
            );

            ActivateJourneyTool();
        }

        private void ActivateJourneyTool()
        {
            if (
                _toolSystem.activeTool ==
                _journeyToolSystem
            )
            {
                Mod.Log.Info(
                    "Journey Planner tool is already active."
                );

                return;
            }

            Mod.Log.Info(
                "Activating Journey Planner tool."
            );

            _toolSystem.activeTool =
                _journeyToolSystem;
        }

        public void ConfirmRoadSelection(
            Entity roadEntity,
            float3 position
        )
        {
            if (
                CurrentSelectionMode ==
                SelectionMode.None
            )
            {
                Mod.Log.Warn(
                    "ConfirmRoadSelection called without " +
                    "an active selection mode."
                );

                return;
            }

            string formattedPosition =
                FormatPosition(position);

            switch (CurrentSelectionMode)
            {
                case SelectionMode.Start:
                    StoreStartRoad(
                        roadEntity,
                        position,
                        formattedPosition
                    );
                    break;

                case SelectionMode.Destination:
                    StoreDestinationRoad(
                        roadEntity,
                        position,
                        formattedPosition
                    );
                    break;

                default:
                    return;
            }

            SetSelectionMode(
                SelectionMode.None
            );

            UpdateReadyStatus();

            _journeyToolSystem
                .ReturnToDefaultTool();
        }

        private void StoreStartRoad(
            Entity roadEntity,
            float3 position,
            string formattedPosition
        )
        {
            StartOwner = roadEntity;
            StartPosition = position;
            HasStart = true;

            _hasStartBinding.Update(true);

            _startPositionBinding.Update(
                formattedPosition
            );

            _startEntityTypeBinding.Update(
                "Road"
            );

            Mod.Log.Info(
                $"Start road selected. " +
                $"Entity={roadEntity}, " +
                $"Position={formattedPosition}"
            );
        }

        private void StoreDestinationRoad(
            Entity roadEntity,
            float3 position,
            string formattedPosition
        )
        {
            DestinationOwner = roadEntity;
            DestinationPosition = position;
            HasDestination = true;

            _hasDestinationBinding.Update(true);

            _destinationPositionBinding.Update(
                formattedPosition
            );

            _destinationEntityTypeBinding.Update(
                "Road"
            );

            Mod.Log.Info(
                $"Destination road selected. " +
                $"Entity={roadEntity}, " +
                $"Position={formattedPosition}"
            );
        }

        public void RejectSelection(
            string reason
        )
        {
            Mod.Log.Warn(
                $"Road selection rejected: {reason}"
            );

            _statusBinding.Update(reason);

            /*
             * The selection mode is deliberately not reset.
             * The custom tool stays active so the user can
             * click another location.
             */
        }

        public void CancelSelection()
        {
            Mod.Log.Info(
                "Map selection cancelled."
            );

            SetSelectionMode(
                SelectionMode.None
            );

            UpdateReadyStatus();
        }

        private void ClearRoute()
        {
            Mod.Log.Info(
                "ClearRoute trigger received."
            );

            HasStart = false;
            HasDestination = false;

            StartOwner = Entity.Null;
            DestinationOwner = Entity.Null;

            StartPosition = default;
            DestinationPosition = default;

            _hasStartBinding.Update(false);
            _hasDestinationBinding.Update(false);

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

            SetSelectionMode(
                SelectionMode.None
            );

            _statusBinding.Update(
                "Journey points cleared"
            );

            _journeyToolSystem
                .ReturnToDefaultTool();

            Mod.Log.Info(
                "Journey points cleared."
            );
        }

        private void CalculateRoute()
        {
            Mod.Log.Info(
                $"CalculateRoute requested. " +
                $"Start={HasStart}, " +
                $"Destination={HasDestination}"
            );

            if (
                !HasStart ||
                !HasDestination
            )
            {
                _statusBinding.Update(
                    "Select both roads before calculating"
                );

                return;
            }

            _statusBinding.Update(
                "Both roads are stored. " +
                "Pathfinding is not implemented yet."
            );

            Mod.Log.Info(
                $"Ready for pathfinding. " +
                $"StartEntity={StartOwner}, " +
                $"Start={FormatPosition(StartPosition)}, " +
                $"DestinationEntity={DestinationOwner}, " +
                $"Destination={FormatPosition(DestinationPosition)}"
            );
        }

        private void UpdateReadyStatus()
        {
            if (
                HasStart &&
                HasDestination
            )
            {
                _statusBinding.Update(
                    "Both roads selected. " +
                    "Ready to calculate route."
                );

                return;
            }

            if (HasStart)
            {
                _statusBinding.Update(
                    "Starting road selected. " +
                    "Select a destination road."
                );

                return;
            }

            if (HasDestination)
            {
                _statusBinding.Update(
                    "Destination road selected. " +
                    "Select a starting road."
                );

                return;
            }

            _statusBinding.Update(
                "Select a starting road"
            );
        }

        private void SetSelectionMode(
            SelectionMode selectionMode
        )
        {
            CurrentSelectionMode =
                selectionMode;

            string bindingValue;

            switch (selectionMode)
            {
                case SelectionMode.Start:
                    bindingValue = "start";
                    break;

                case SelectionMode.Destination:
                    bindingValue =
                        "destination";
                    break;

                default:
                    bindingValue = "none";
                    break;
            }

            _selectionModeBinding.Update(
                bindingValue
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
    }
}