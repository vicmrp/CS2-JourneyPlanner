using Colossal.UI.Binding;
using Game.UI;

namespace CS2_JourneyPlanner
{
    public sealed partial class JourneyPlannerUISystem : UISystemBase
    {
        private const string BindingGroup = "JourneyPlanner";

        private ValueBinding<string> _connectionStatusBinding;
        private ValueBinding<int> _contactCountBinding;

        private int _contactCount;

        protected override void OnCreate()
        {
            base.OnCreate();

            Mod.log.Info("JourneyPlannerUISystem.OnCreate.");

            _connectionStatusBinding = new ValueBinding<string>(
                BindingGroup,
                "ConnectionStatus",
                "C# binding created"
            );

            _contactCountBinding = new ValueBinding<int>(
                BindingGroup,
                "ContactCount",
                0
            );

            AddBinding(_connectionStatusBinding);
            AddBinding(_contactCountBinding);

            AddBinding(
                new TriggerBinding(
                    BindingGroup,
                    "TestContact",
                    TestContact
                )
            );

            AddBinding(
                new TriggerBinding(
                    BindingGroup,
                    "ResetContact",
                    ResetContact
                )
            );

            Mod.log.Info("Journey Planner UI bindings added.");
        }

        private void TestContact()
        {
            _contactCount++;

            string message =
                $"Contact confirmed. React called C# {_contactCount} time(s).";

            Mod.log.Info(message);

            _contactCountBinding.Update(_contactCount);
            _connectionStatusBinding.Update(message);
        }

        private void ResetContact()
        {
            _contactCount = 0;

            Mod.log.Info("Contact test reset.");

            _contactCountBinding.Update(0);
            _connectionStatusBinding.Update("Contact test reset");
        }
    }
}