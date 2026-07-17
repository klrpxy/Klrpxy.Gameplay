namespace Klrpxy.Gameplay.Stats
{
    public sealed class RangeStat
    {
        private readonly System.Collections.Generic.List<Stat.ModifierRegistration> modifiers = new System.Collections.Generic.List<Stat.ModifierRegistration>();
        private readonly RoundingMode rounding;
        private float? minimumBound;
        private float? maximumBound;
        private FloatRange finalRange;
        private readonly GameplayThreadGuard threadGuard = new GameplayThreadGuard();
        private ValueInput<float> minimumInput;
        private ValueInput<float> maximumInput;
        private System.IDisposable minimumSubscription;
        private System.IDisposable maximumSubscription;
        private System.IDisposable minimumDependency;
        private System.IDisposable maximumDependency;
        private bool disposed;

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

        public FloatRange FinalRange
        {
            get
            {
                ThrowIfDisposed();
                return finalRange;
            }
        }

        public event System.Action<FloatRange, FloatRange> OnFinalRangeChanged;

        internal event System.Action FinalRangeChanged;

        public RangeStat WithMinimum(float minimum)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            Modifier.ValidateFinite(minimum, nameof(minimum));
            float rounded = rounding == RoundingMode.None ? minimum : (float)System.Math.Ceiling(minimum);
            float? nextMaximum = ReadCurrentMaximum();
            if (nextMaximum.HasValue && rounded > nextMaximum.Value)
            {
                throw new System.ArgumentOutOfRangeException(nameof(minimum));
            }

            System.IDisposable previousSubscription = minimumSubscription;
            System.IDisposable previousDependency = minimumDependency;
            minimumInput = null;
            minimumBound = rounded;
            maximumBound = nextMaximum;
            minimumSubscription = null;
            minimumDependency = null;
            Recalculate();
            previousSubscription?.Dispose();
            previousDependency?.Dispose();
            return this;
        }

        public RangeStat WithMaximum(float maximum)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            Modifier.ValidateFinite(maximum, nameof(maximum));
            float rounded = rounding == RoundingMode.None ? maximum : (float)System.Math.Floor(maximum);
            float? nextMinimum = ReadCurrentMinimum();
            if (nextMinimum.HasValue && nextMinimum.Value > rounded)
            {
                throw new System.ArgumentOutOfRangeException(nameof(maximum));
            }

            System.IDisposable previousSubscription = maximumSubscription;
            System.IDisposable previousDependency = maximumDependency;
            maximumInput = null;
            minimumBound = nextMinimum;
            maximumBound = rounded;
            maximumSubscription = null;
            maximumDependency = null;
            Recalculate();
            previousSubscription?.Dispose();
            previousDependency?.Dispose();
            return this;
        }

        public RangeStat WithMinimum(ValueInput<float> minimum)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            if (minimum == null) throw new System.ArgumentNullException(nameof(minimum));
            float nextMinimum = RoundMinimum(minimum.Read());
            float? nextMaximum = ReadCurrentMaximum();
            if (nextMaximum.HasValue && nextMinimum > nextMaximum.Value)
            {
                throw new System.ArgumentOutOfRangeException(nameof(minimum));
            }

            System.IDisposable nextDependency = StatsPropagationCoordinator.AddDependencies(
                minimum.DependencyNode == null ? System.Array.Empty<object>() : new[] { minimum.DependencyNode },
                this);
            System.IDisposable nextSubscription = null;
            try
            {
                nextSubscription = minimum.Subscribe(UpdateDynamicEndpoints);
                System.IDisposable previousSubscription = minimumSubscription;
                System.IDisposable previousDependency = minimumDependency;
                minimumInput = minimum;
                minimumBound = nextMinimum;
                maximumBound = nextMaximum;
                minimumSubscription = nextSubscription;
                minimumDependency = nextDependency;
                Recalculate();
                previousSubscription?.Dispose();
                previousDependency?.Dispose();
                return this;
            }
            catch
            {
                nextSubscription?.Dispose();
                nextDependency.Dispose();
                throw;
            }
        }

        public RangeStat WithMaximum(ValueInput<float> maximum)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            if (maximum == null) throw new System.ArgumentNullException(nameof(maximum));
            float nextMaximum = RoundMaximum(maximum.Read());
            float? nextMinimum = ReadCurrentMinimum();
            if (nextMinimum.HasValue && nextMinimum.Value > nextMaximum)
            {
                throw new System.ArgumentOutOfRangeException(nameof(maximum));
            }

            System.IDisposable nextDependency = StatsPropagationCoordinator.AddDependencies(
                maximum.DependencyNode == null ? System.Array.Empty<object>() : new[] { maximum.DependencyNode },
                this);
            System.IDisposable nextSubscription = null;
            try
            {
                nextSubscription = maximum.Subscribe(UpdateDynamicEndpoints);
                System.IDisposable previousSubscription = maximumSubscription;
                System.IDisposable previousDependency = maximumDependency;
                maximumInput = maximum;
                minimumBound = nextMinimum;
                maximumBound = nextMaximum;
                maximumSubscription = nextSubscription;
                maximumDependency = nextDependency;
                Recalculate();
                previousSubscription?.Dispose();
                previousDependency?.Dispose();
                return this;
            }
            catch
            {
                nextSubscription?.Dispose();
                nextDependency.Dispose();
                throw;
            }
        }

        private void UpdateDynamicEndpoints()
        {
            float? nextMinimum = ReadCurrentMinimum();
            float? nextMaximum = ReadCurrentMaximum();
            if (nextMinimum.HasValue && nextMaximum.HasValue && nextMinimum.Value > nextMaximum.Value)
            {
                throw new System.InvalidOperationException("The dynamic minimum cannot exceed the maximum.");
            }

            minimumBound = nextMinimum;
            maximumBound = nextMaximum;
            Recalculate();
        }

        private float? ReadCurrentMinimum()
        {
            return minimumInput == null ? minimumBound : RoundMinimum(minimumInput.Read());
        }

        private float? ReadCurrentMaximum()
        {
            return maximumInput == null ? maximumBound : RoundMaximum(maximumInput.Read());
        }

        private float RoundMinimum(float minimum)
        {
            Modifier.ValidateFinite(minimum, nameof(minimum));
            return rounding == RoundingMode.None ? minimum : (float)System.Math.Ceiling(minimum);
        }

        private float RoundMaximum(float maximum)
        {
            Modifier.ValidateFinite(maximum, nameof(maximum));
            return rounding == RoundingMode.None ? maximum : (float)System.Math.Floor(maximum);
        }

        internal ModifierHandle AddModifier(Modifier modifier, ModifierSource source, long order)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
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

        internal void AddConditionalRegistration(Stat.ModifierRegistration registration)
        {
            modifiers.Add(registration);
            try
            {
                Recalculate();
            }
            catch
            {
                modifiers.Remove(registration);
                registration.Dispose();
                throw;
            }
        }

        internal void RemoveConditionalRegistration(Stat.ModifierRegistration registration)
        {
            modifiers.Remove(registration);
            registration.Dispose();
        }

        private void Recalculate()
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            StatsPropagationCoordinator.Execute(RecalculateCore);
        }

        internal void RecalculateForCoordinator()
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            RecalculateCore();
        }

        internal void VerifyThread()
        {
            threadGuard.Verify();
            ThrowIfDisposed();
        }

        internal void Dispose()
        {
            threadGuard.Verify();
            if (disposed) return;
            disposed = true;
            foreach (Stat.ModifierRegistration registration in modifiers) registration.Dispose();
            modifiers.Clear();
            minimumSubscription?.Dispose();
            maximumSubscription?.Dispose();
            minimumDependency?.Dispose();
            maximumDependency?.Dispose();
            StatsPropagationCoordinator.RemoveNode(this);
            minimumSubscription = null;
            maximumSubscription = null;
            minimumDependency = null;
            maximumDependency = null;
            FinalRangeChanged = null;
            OnFinalRangeChanged = null;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new System.ObjectDisposedException(nameof(RangeStat));
        }

        private void RecalculateCore()
        {
            FloatRange previous = finalRange;
            var allModifiers = new System.Collections.Generic.List<IModifierEntry>();
            foreach (Stat.ModifierRegistration modifier in modifiers) allModifiers.Add(modifier);
            StatSet?.Subject?.AppendGroupModifiers(this, allModifiers);
            allModifiers.Sort((left, right) => left.Order.CompareTo(right.Order));
            FloatRange range = new FloatRange(
                ModifierCalculation.CalculateArithmetic(BaseRange.Min, allModifiers),
                ModifierCalculation.CalculateArithmetic(BaseRange.Max, allModifiers));
            IModifierEntry overrideRegistration = Stat.SelectWinning(allModifiers, ModifierKind.Override);
            if (overrideRegistration != null)
            {
                range = overrideRegistration.Modifier.Range;
            }

            range = Sort(range);
            range = Sort(new FloatRange(
                ModifierCalculation.Round(range.Min, rounding),
                ModifierCalculation.Round(range.Max, rounding)));
            FloatRange? clamp = Stat.CombineClamps(allModifiers);
            if (clamp.HasValue)
            {
                range = Clamp(range, clamp.Value);
            }

            if (minimumBound.HasValue)
            {
                range = Sort(new FloatRange(
                    System.Math.Max(range.Min, minimumBound.Value),
                    System.Math.Max(range.Max, minimumBound.Value)));
            }

            if (maximumBound.HasValue)
            {
                range = Sort(new FloatRange(
                    System.Math.Min(range.Min, maximumBound.Value),
                    System.Math.Min(range.Max, maximumBound.Value)));
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
