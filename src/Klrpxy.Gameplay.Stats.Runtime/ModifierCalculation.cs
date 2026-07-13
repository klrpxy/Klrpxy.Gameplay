using System;
using System.Collections.Generic;

namespace Klrpxy.Gameplay.Stats
{
    internal static class ModifierCalculation
    {
        internal static float CalculateArithmetic(
            float baseValue,
            IReadOnlyList<Stat.ModifierRegistration> modifiers)
        {
            float result = baseValue;
            foreach (Stat.ModifierRegistration registration in modifiers)
            {
                if (registration.Modifier.Kind == ModifierKind.Flat)
                {
                    result += registration.Modifier.Value;
                }
            }

            float percent = 0f;
            foreach (Stat.ModifierRegistration registration in modifiers)
            {
                if (registration.Modifier.Kind == ModifierKind.Percent)
                {
                    percent += registration.Modifier.Value;
                }
            }

            result *= 1f + percent;
            foreach (Stat.ModifierRegistration registration in modifiers)
            {
                if (registration.Modifier.Kind == ModifierKind.Multiply)
                {
                    result *= registration.Modifier.Value;
                }
            }

            Modifier.ValidateFinite(result, "calculation");
            return result;
        }

        internal static float Round(float value, RoundingMode rounding)
        {
            switch (rounding)
            {
                case RoundingMode.Floor: return (float)Math.Floor(value);
                case RoundingMode.Ceiling: return (float)Math.Ceiling(value);
                case RoundingMode.Nearest: return (float)Math.Round(value, MidpointRounding.AwayFromZero);
                default: return value;
            }
        }
    }
}
