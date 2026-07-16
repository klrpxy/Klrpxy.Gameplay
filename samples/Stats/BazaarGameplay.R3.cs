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
            source.Modify(hero.StatSet.Power).Add(dynamicValue);
            dynamicValue.Value = 8f;
            source.Modify(hero.StatSet.Power).Where(condition).Add(10f);
            condition.Value = true;

            bool passed = hero.StatSet.Power.FinalValue == 28f
                && observed == 28f
                && observationCount == 4;
            source.Dispose();
            hero.Dispose();
            return passed;
        }
    }
}
