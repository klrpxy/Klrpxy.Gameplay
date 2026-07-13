using System;
using System.Collections.Generic;

namespace Klrpxy.Gameplay.Stats
{
    public sealed class Stat
    {
        internal StatSet StatSet { get; set; }

        private float baseValue;
        private readonly List<ModifierRegistration> modifiers = new List<ModifierRegistration>();
        private readonly RoundingMode rounding;
        private FloatRange? bounds;
        private float finalValue;

        public Stat(float baseValue, RoundingMode rounding = RoundingMode.None)
        {
            this.rounding = rounding;
            BaseValue = baseValue;
        }

        public float BaseValue
        {
            get => baseValue;
            set
            {
                Modifier.ValidateFinite(value, nameof(value));
                if (baseValue == value)
                {
                    return;
                }

                baseValue = value;
                Recalculate();
            }
        }

        public float FinalValue => finalValue;

        public event Action<float, float> OnFinalValueChanged;

        public Stat WithBounds(float minimum, float maximum)
        {
            Modifier.ValidateFinite(minimum, nameof(minimum));
            Modifier.ValidateFinite(maximum, nameof(maximum));
            if (minimum > maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(minimum));
            }

            bounds = new FloatRange(
                rounding == RoundingMode.None ? minimum : (float)Math.Ceiling(minimum),
                rounding == RoundingMode.None ? maximum : (float)Math.Floor(maximum));
            if (bounds.Value.Min > bounds.Value.Max)
            {
                throw new ArgumentOutOfRangeException(nameof(minimum));
            }

            Recalculate();
            return this;
        }

        internal ModifierHandle AddModifier(Modifier modifier, ModifierSource source, long order)
        {
            var registration = new ModifierRegistration(modifier, order);
            ModifierHandle handle = null;
            handle = new ModifierHandle(source, ignored =>
            {
                modifiers.Remove(registration);
            }, Recalculate);
            source.Add(handle);
            modifiers.Add(registration);
            Recalculate();
            return handle;
        }

        private void Recalculate()
        {
            float previous = finalValue;
            float calculated = ModifierCalculation.CalculateArithmetic(baseValue, modifiers);

            ModifierRegistration overrideRegistration = SelectWinning(modifiers, ModifierKind.Override);
            if (overrideRegistration != null)
            {
                calculated = overrideRegistration.Modifier.Value;
            }

            calculated = ModifierCalculation.Round(calculated, rounding);
            FloatRange? clamp = CombineClamps(modifiers);
            if (clamp.HasValue)
            {
                calculated = Clamp(calculated, clamp.Value);
            }

            if (bounds.HasValue)
            {
                calculated = Clamp(calculated, bounds.Value);
            }

            Modifier.ValidateFinite(calculated, "calculation");
            finalValue = calculated;
            if (previous != finalValue)
            {
                OnFinalValueChanged?.Invoke(previous, finalValue);
            }
        }

        internal static ModifierRegistration SelectWinning(List<ModifierRegistration> registrations, ModifierKind kind)
        {
            ModifierRegistration result = null;
            foreach (ModifierRegistration registration in registrations)
            {
                if (registration.Modifier.Kind != kind
                    || (result != null && (registration.Modifier.Priority < result.Modifier.Priority
                        || (registration.Modifier.Priority == result.Modifier.Priority && registration.Order < result.Order))))
                {
                    continue;
                }

                result = registration;
            }

            return result;
        }

        internal static FloatRange? CombineClamps(List<ModifierRegistration> registrations)
        {
            var clamps = new List<ModifierRegistration>();
            foreach (ModifierRegistration registration in registrations)
            {
                if (registration.Modifier.Kind == ModifierKind.Clamp)
                {
                    clamps.Add(registration);
                }
            }

            if (clamps.Count == 0)
            {
                return null;
            }

            float minimum = float.NegativeInfinity;
            float maximum = float.PositiveInfinity;
            foreach (ModifierRegistration clamp in clamps)
            {
                minimum = Math.Max(minimum, clamp.Modifier.Range.Min);
                maximum = Math.Min(maximum, clamp.Modifier.Range.Max);
            }

            if (minimum <= maximum)
            {
                return new FloatRange(minimum, maximum);
            }

            return SelectWinning(clamps, ModifierKind.Clamp).Modifier.Range;
        }

        internal static float Clamp(float value, FloatRange range) => Math.Min(Math.Max(value, range.Min), range.Max);

        internal sealed class ModifierRegistration
        {
            public ModifierRegistration(Modifier modifier, long order)
            {
                Modifier = modifier;
                Order = order;
            }

            public Modifier Modifier { get; }

            public long Order { get; }
        }
    }
}
