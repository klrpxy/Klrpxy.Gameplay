namespace Klrpxy.Gameplay.Stats
{
    public sealed class RangeStat
    {
        private readonly System.Collections.Generic.List<Stat.ModifierRegistration> modifiers = new System.Collections.Generic.List<Stat.ModifierRegistration>();
        private readonly RoundingMode rounding;
        private FloatRange? bounds;
        private FloatRange finalRange;

        internal StatSet StatSet { get; set; }

        public RangeStat(float minimum, float maximum, RoundingMode rounding = RoundingMode.None)
        {
            Modifier.ValidateFinite(minimum, nameof(minimum));
            Modifier.ValidateFinite(maximum, nameof(maximum));
            this.rounding = rounding;
            BaseRange = new FloatRange(minimum, maximum);
            Recalculate();
        }

        public FloatRange BaseRange { get; }

        public FloatRange FinalRange => finalRange;

        public event System.Action<FloatRange, FloatRange> OnFinalRangeChanged;

        public RangeStat WithBounds(float minimum, float maximum)
        {
            Modifier.ValidateFinite(minimum, nameof(minimum));
            Modifier.ValidateFinite(maximum, nameof(maximum));
            if (minimum > maximum)
            {
                throw new System.ArgumentOutOfRangeException(nameof(minimum));
            }

            bounds = new FloatRange(
                rounding == RoundingMode.None ? minimum : (float)System.Math.Ceiling(minimum),
                rounding == RoundingMode.None ? maximum : (float)System.Math.Floor(maximum));
            if (bounds.Value.Min > bounds.Value.Max)
            {
                throw new System.ArgumentOutOfRangeException(nameof(minimum));
            }

            Recalculate();
            return this;
        }

        internal ModifierHandle AddModifier(Modifier modifier, ModifierSource source, long order)
        {
            var registration = new Stat.ModifierRegistration(modifier, order);
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
            FloatRange previous = finalRange;
            FloatRange range = new FloatRange(
                ModifierCalculation.CalculateArithmetic(BaseRange.Min, modifiers),
                ModifierCalculation.CalculateArithmetic(BaseRange.Max, modifiers));
            Stat.ModifierRegistration overrideRegistration = Stat.SelectWinning(modifiers, ModifierKind.Override);
            if (overrideRegistration != null)
            {
                range = overrideRegistration.Modifier.Range;
            }

            range = Sort(range);
            range = Sort(new FloatRange(
                ModifierCalculation.Round(range.Min, rounding),
                ModifierCalculation.Round(range.Max, rounding)));
            FloatRange? clamp = Stat.CombineClamps(modifiers);
            if (clamp.HasValue)
            {
                range = Clamp(range, clamp.Value);
            }

            if (bounds.HasValue)
            {
                range = Clamp(range, bounds.Value);
            }

            finalRange = range;
            if (previous.Min != finalRange.Min || previous.Max != finalRange.Max)
            {
                OnFinalRangeChanged?.Invoke(previous, finalRange);
            }
        }

        private static FloatRange Sort(FloatRange range)
        {
            return range.Min <= range.Max ? range : new FloatRange(range.Max, range.Min);
        }

        private static FloatRange Clamp(FloatRange range, FloatRange bounds)
        {
            return Sort(new FloatRange(Stat.Clamp(range.Min, bounds), Stat.Clamp(range.Max, bounds)));
        }
    }
}
