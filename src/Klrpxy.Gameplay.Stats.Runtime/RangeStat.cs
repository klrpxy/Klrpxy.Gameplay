namespace Klrpxy.Gameplay.Stats
{
    public sealed class RangeStat
    {
        private readonly System.Collections.Generic.List<Stat.ModifierRegistration> modifiers = new System.Collections.Generic.List<Stat.ModifierRegistration>();
        private readonly RoundingMode rounding;
        private FloatRange? bounds;
        private FloatRange finalRange;
        private readonly GameplayThreadGuard threadGuard = new GameplayThreadGuard();
        private ValueInput<float> minimumInput;
        private ValueInput<float> maximumInput;
        private System.IDisposable minimumSubscription;
        private System.IDisposable maximumSubscription;
        private System.IDisposable boundsDependency;

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

        internal event System.Action FinalRangeChanged;

        public RangeStat WithBounds(float minimum, float maximum)
        {
            threadGuard.Verify();
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

        public RangeStat WithBounds(ValueInput<float> minimum, ValueInput<float> maximum)
        {
            threadGuard.Verify();
            if (minimum == null) throw new System.ArgumentNullException(nameof(minimum));
            if (maximum == null) throw new System.ArgumentNullException(nameof(maximum));
            FloatRange initialBounds = CreateBounds(minimum.Read(), maximum.Read());
            var nodes = new System.Collections.Generic.List<object>();
            if (minimum.DependencyNode != null) nodes.Add(minimum.DependencyNode);
            if (maximum.DependencyNode != null) nodes.Add(maximum.DependencyNode);
            boundsDependency = StatsPropagationCoordinator.AddDependencies(nodes, this);
            try
            {
                minimumInput = minimum;
                maximumInput = maximum;
                bounds = initialBounds;
                minimumSubscription = minimum.Subscribe(UpdateDynamicBounds);
                maximumSubscription = maximum.Subscribe(UpdateDynamicBounds);
                Recalculate();
                return this;
            }
            catch
            {
                boundsDependency.Dispose();
                boundsDependency = null;
                throw;
            }
        }

        private void UpdateDynamicBounds()
        {
            bounds = CreateBounds(minimumInput.Read(), maximumInput.Read());
            Recalculate();
        }

        private FloatRange CreateBounds(float minimum, float maximum)
        {
            Modifier.ValidateFinite(minimum, nameof(minimum));
            Modifier.ValidateFinite(maximum, nameof(maximum));
            if (minimum > maximum) throw new System.ArgumentOutOfRangeException(nameof(minimum));
            var result = new FloatRange(
                rounding == RoundingMode.None ? minimum : (float)System.Math.Ceiling(minimum),
                rounding == RoundingMode.None ? maximum : (float)System.Math.Floor(maximum));
            if (result.Min > result.Max) throw new System.ArgumentOutOfRangeException(nameof(minimum));
            return result;
        }

        internal ModifierHandle AddModifier(Modifier modifier, ModifierSource source, long order)
        {
            threadGuard.Verify();
            var registration = new Stat.ModifierRegistration(modifier, order);
            ModifierHandle handle = null;
            handle = new ModifierHandle(source, ignored =>
            {
                modifiers.Remove(registration);
                registration.Dispose();
            }, Recalculate);
            registration.Subscribe(Recalculate, this);
            source.Add(handle);
            modifiers.Add(registration);
            Recalculate();
            return handle;
        }

        private void Recalculate()
        {
            threadGuard.Verify();
            StatsPropagationCoordinator.Execute(RecalculateCore);
        }

        internal void VerifyThread() => threadGuard.Verify();

        private void RecalculateCore()
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
                FinalRangeChanged?.Invoke();
                StatsPropagationCoordinator.RecordChange(this, () => OnFinalRangeChanged, previous, finalRange);
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
