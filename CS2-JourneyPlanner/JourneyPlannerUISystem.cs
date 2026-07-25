using Colossal.UI.Binding;
using Game.UI;

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
        private ValueBinding<string> _selectionModeBinding;
        private ValueBinding<bool> _hasStartBinding;
        private ValueBinding<bool> _hasDestinationBinding;

        public SelectionMode CurrentSelectionMode { get; private set; }

        public bool HasStart { get; private set; }
        public bool HasDestination { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();

            _selectionModeBinding = new ValueBinding<string>(
                "JourneyPlanner",
                "SelectionMode",
                "none"
            );

            _hasStartBinding = new ValueBinding<bool>(
                "JourneyPlanner",
                "HasStart",
                false
            );

            _hasDestinationBinding = new ValueBinding<bool>(
                "JourneyPlanner",
                "HasDestination",
                false
            );

            AddBinding(_selectionModeBinding);
            AddBinding(_hasStartBinding);
            AddBinding(_hasDestinationBinding);

            AddBinding(new TriggerBinding(
                "JourneyPlanner",
                "SelectStart",
                SelectStart
            ));

            AddBinding(new TriggerBinding(
                "JourneyPlanner",
                "SelectDestination",
                SelectDestination
            ));

            AddBinding(new TriggerBinding(
                "JourneyPlanner",
                "ClearRoute",
                ClearRoute
            ));

            AddBinding(new TriggerBinding(
                "JourneyPlanner",
                "CalculateRoute",
                CalculateRoute
            ));
        }

        private void SelectStart()
        {
            SetSelectionMode(SelectionMode.Start);
            Mod.log.Info("Waiting for start-point selection.");
        }

        private void SelectDestination()
        {
            SetSelectionMode(SelectionMode.Destination);
            Mod.log.Info("Waiting for destination selection.");
        }

        private void ClearRoute()
        {
            HasStart = false;
            HasDestination = false;

            _hasStartBinding.Update(false);
            _hasDestinationBinding.Update(false);

            SetSelectionMode(SelectionMode.None);

            Mod.log.Info("Journey points cleared.");
        }

        private void CalculateRoute()
        {
            Mod.log.Info(
                $"Calculate requested. Start={HasStart}, Destination={HasDestination}"
            );
        }

        public void ConfirmSelection()
        {
            switch (CurrentSelectionMode)
            {
                case SelectionMode.Start:
                    HasStart = true;
                    _hasStartBinding.Update(true);
                    break;

                case SelectionMode.Destination:
                    HasDestination = true;
                    _hasDestinationBinding.Update(true);
                    break;

                default:
                    return;
            }

            SetSelectionMode(SelectionMode.None);
        }

        private void SetSelectionMode(SelectionMode mode)
        {
            CurrentSelectionMode = mode;

            string uiValue = mode switch
            {
                SelectionMode.Start => "start",
                SelectionMode.Destination => "destination",
                _ => "none"
            };

            _selectionModeBinding.Update(uiValue);
        }
    }
}