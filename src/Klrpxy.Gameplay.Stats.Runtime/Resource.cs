using System;

namespace Klrpxy.Gameplay.Stats
{
    public sealed class Resource
    {
        private readonly RoundingMode rounding;
        private bool hasMinimum;
        private bool hasMaximum;
        private float maximum;
        private float minimum;
        private float value;
        private bool preserveRatioWhenBoundsChange;
        private ValueInput<float> maximumInput;
        private IDisposable maximumSubscription;
        private IDisposable maximumDependency;
        private IDisposable minimumDependency;
        private ValueInput<float> minimumInput;
        private IDisposable minimumSubscription;
        private readonly GameplayThreadGuard threadGuard = new GameplayThreadGuard();
        private bool disposed;

        internal StatSet StatSet { get; set; }

        public Resource(float value, RoundingMode rounding = RoundingMode.None)
        {
            EnsureFinite(value);
            this.rounding = rounding;
            this.value = value;
        }

        public float Value
        {
            get
            {
                ThrowIfDisposed();
                return value;
            }
        }

        public event Action<float, float> OnValueChanged;

        internal event Action ValueChanged;

        public Resource PreserveRatioWhenBoundsChange()
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            preserveRatioWhenBoundsChange = true;
            return this;
        }

        public void Set(float value)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            StatsPropagationCoordinator.Execute(() => SetCore(value));
        }

        private void SetCore(float value)
        {
            EnsureFinite(value);
            value = ApplyRounding(value);
            value = ApplyBounds(value);

            if (this.value == value)
            {
                return;
            }

            float previous = this.value;
            this.value = value;
            ValueChanged?.Invoke();
            StatsPropagationCoordinator.RecordChange(this, () => OnValueChanged, previous, value);
        }

        public void Increase(float amount)
        {
            Set(value + amount);
        }

        public void Decrease(float amount)
        {
            Set(value - amount);
        }

        public Resource WithMinimum(float minimum)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            EnsureFinite(minimum);
            float nextMaximum = ReadCurrentMaximum();
            if (hasMaximum && minimum > nextMaximum)
            {
                throw new ArgumentOutOfRangeException(nameof(minimum), "The minimum cannot exceed the maximum.");
            }

            IDisposable previousSubscription = minimumSubscription;
            IDisposable previousDependency = minimumDependency;
            minimumInput = null;
            this.minimum = minimum;
            maximum = nextMaximum;
            hasMinimum = true;
            minimumSubscription = null;
            minimumDependency = null;
            StatsPropagationCoordinator.Execute(ApplyDeclaredBoundsCore);
            previousSubscription?.Dispose();
            previousDependency?.Dispose();
            return this;
        }

        public Resource WithMaximum(float maximum)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            EnsureFinite(maximum);
            float nextMinimum = ReadCurrentMinimum();
            if (hasMinimum && nextMinimum > maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum), "The maximum cannot be below the minimum.");
            }

            IDisposable previousSubscription = maximumSubscription;
            IDisposable previousDependency = maximumDependency;
            maximumInput = null;
            minimum = nextMinimum;
            this.maximum = maximum;
            hasMaximum = true;
            maximumSubscription = null;
            maximumDependency = null;
            StatsPropagationCoordinator.Execute(ApplyDeclaredBoundsCore);
            previousSubscription?.Dispose();
            previousDependency?.Dispose();
            return this;
        }

        public Resource WithMaximum(ValueInput<float> maximum)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            if (maximum == null) throw new ArgumentNullException(nameof(maximum));
            float nextMaximum = maximum.Read();
            EnsureFinite(nextMaximum);
            float nextMinimum = ReadCurrentMinimum();
            if (hasMinimum && nextMinimum > nextMaximum)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum), "The maximum cannot be below the minimum.");
            }

            IDisposable nextDependency = StatsPropagationCoordinator.AddDependencies(
                maximum.DependencyNode == null ? Array.Empty<object>() : new[] { maximum.DependencyNode },
                this);
            IDisposable nextSubscription = null;
            try
            {
                nextSubscription = maximum.Subscribe(UpdateDynamicEndpoints);
                IDisposable previousSubscription = maximumSubscription;
                IDisposable previousDependency = maximumDependency;
                maximumInput = maximum;
                minimum = nextMinimum;
                this.maximum = nextMaximum;
                hasMaximum = true;
                maximumSubscription = nextSubscription;
                maximumDependency = nextDependency;
                StatsPropagationCoordinator.Execute(ApplyDeclaredBoundsCore);
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

        public Resource WithMinimum(ValueInput<float> minimum)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            if (minimum == null) throw new ArgumentNullException(nameof(minimum));
            float nextMinimum = minimum.Read();
            EnsureFinite(nextMinimum);
            float nextMaximum = ReadCurrentMaximum();
            if (hasMaximum && nextMinimum > nextMaximum)
            {
                throw new ArgumentOutOfRangeException(nameof(minimum), "The minimum cannot exceed the maximum.");
            }

            IDisposable nextDependency = StatsPropagationCoordinator.AddDependencies(
                minimum.DependencyNode == null ? Array.Empty<object>() : new[] { minimum.DependencyNode },
                this);
            IDisposable nextSubscription = null;
            try
            {
                nextSubscription = minimum.Subscribe(UpdateDynamicEndpoints);
                IDisposable previousSubscription = minimumSubscription;
                IDisposable previousDependency = minimumDependency;
                minimumInput = minimum;
                this.minimum = nextMinimum;
                maximum = nextMaximum;
                hasMinimum = true;
                minimumSubscription = nextSubscription;
                minimumDependency = nextDependency;
                StatsPropagationCoordinator.Execute(ApplyDeclaredBoundsCore);
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

        private void ApplyDeclaredBoundsCore()
        {
            float bounded = ApplyBounds(value);
            if (value == bounded) return;

            float previous = value;
            value = bounded;
            ValueChanged?.Invoke();
            StatsPropagationCoordinator.RecordChange(this, () => OnValueChanged, previous, bounded);
        }

        private void UpdateDynamicEndpoints()
        {
            float previousMinimum = minimum;
            float previousMaximum = maximum;
            float nextMinimum = ReadCurrentMinimum();
            float nextMaximum = ReadCurrentMaximum();
            if (hasMinimum && hasMaximum && nextMinimum > nextMaximum)
            {
                throw new InvalidOperationException("The dynamic minimum cannot exceed the maximum.");
            }

            minimum = nextMinimum;
            maximum = nextMaximum;
            if (preserveRatioWhenBoundsChange
                && hasMinimum
                && hasMaximum
                && previousMaximum != previousMinimum)
            {
                float ratio = (value - previousMinimum) / (previousMaximum - previousMinimum);
                Set(nextMinimum + ((nextMaximum - nextMinimum) * ratio));
                return;
            }

            Set(value);
        }

        private float ReadCurrentMinimum()
        {
            float current = minimumInput == null ? minimum : minimumInput.Read();
            EnsureFinite(current);
            return current;
        }

        private float ReadCurrentMaximum()
        {
            float current = maximumInput == null ? maximum : maximumInput.Read();
            EnsureFinite(current);
            return current;
        }

        private static void EnsureFinite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The value must be finite.");
            }
        }

        private float ApplyRounding(float value)
        {
            switch (rounding)
            {
                case RoundingMode.Floor:
                    return (float)Math.Floor(value);
                case RoundingMode.Ceiling:
                    return (float)Math.Ceiling(value);
                case RoundingMode.Nearest:
                    return (float)Math.Round(value, MidpointRounding.AwayFromZero);
                default:
                    return value;
            }
        }

        private float ApplyBounds(float value)
        {
            if (!hasMinimum && !hasMaximum)
            {
                return value;
            }

            if (hasMinimum)
            {
                value = Math.Max(value, minimum);
            }
            return hasMaximum ? Math.Min(value, maximum) : value;
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
            minimumSubscription?.Dispose();
            maximumSubscription?.Dispose();
            maximumDependency?.Dispose();
            minimumDependency?.Dispose();
            StatsPropagationCoordinator.RemoveNode(this);
            minimumSubscription = null;
            maximumSubscription = null;
            maximumDependency = null;
            minimumDependency = null;
            ValueChanged = null;
            OnValueChanged = null;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(Resource));
        }
    }
}
