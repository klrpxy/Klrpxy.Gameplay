using Klrpxy.Gameplay.Stats;
using Klrpxy.Gameplay.Stats.R3;
using R3;

namespace Consumer
{
    public static class BazaarR3ConsumerContract
    {
        public static bool VerifyR3ConditionsAndObservation()
        {
            var hero = new Hero();
            var source = new ModifierSource();
            var dynamicValue = new ReactiveProperty<float>(5f);
            var condition = new ReactiveProperty<bool>(false);
            float observed = -1f;
            int observationCount = 0;

            hero.StatSet.Power.ObserveFinalValue().Subscribe(value =>
            {
                observed = value;
                observationCount++;
            });
            source.Modify(hero.StatSet.Power).Where(condition).Add(dynamicValue);
            dynamicValue.Value = 8f;
            condition.Value = true;

            bool passed = hero.StatSet.Power.FinalValue == 18f
                && observed == 18f
                && observationCount == 2;
            source.Dispose();
            hero.Dispose();
            return passed;
        }
    }
}
