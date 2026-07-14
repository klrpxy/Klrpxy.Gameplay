using System;

namespace Klrpxy.Gameplay.Stats
{
    public sealed class Resource
    {
        private readonly RoundingMode rounding;
        private bool hasBounds;
        private bool hasMaximum;
        private float maximum;
        private float minimum;
        private float value;
        private ResourceBoundPolicy boundPolicy;
        private ValueInput<float> maximumInput;
        private IDisposable maximumSubscription;
        private IDisposable boundsDependency;
        private ValueInput<float> minimumInput;
        private IDisposable minimumSubscription;
        private readonly GameplayThreadGuard threadGuard = new GameplayThreadGuard();

        internal StatSet StatSet { get; set; }

        public Resource(float value, RoundingMode rounding = RoundingMode.None)
        {
            EnsureFinite(value);
            this.rounding = rounding;
            this.value = value;
        }

        public float Value => value;

        public event Action<float, float> OnValueChanged;

        internal event Action ValueChanged;

        public void Set(float value)
        {
            threadGuard.Verify();
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

        public Resource WithBounds(float minimum, float maximum)
        {
            threadGuard.Verify();
            EnsureBoundsAreNotDeclared();
            EnsureFinite(minimum);
            EnsureFinite(maximum);

            if (minimum > maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(minimum), "The minimum cannot exceed the maximum.");
            }

            this.minimum = minimum;
            this.maximum = maximum;
            hasBounds = true;
            hasMaximum = true;
            StatsPropagationCoordinator.Execute(ApplyDeclaredBoundsCore);
            return this;
        }

        public Resource WithMinimum(float minimum)
        {
            threadGuard.Verify();
            EnsureBoundsAreNotDeclared();
            EnsureFinite(minimum);
            this.minimum = minimum;
            hasBounds = true;
            hasMaximum = false;
            StatsPropagationCoordinator.Execute(ApplyDeclaredBoundsCore);
            return this;
        }

        public Resource WithBounds(float minimum, ValueInput<float> maximum, ResourceBoundPolicy policy = ResourceBoundPolicy.Clamp)
        {
            threadGuard.Verify();
            EnsureBoundsAreNotDeclared();
            if (maximum == null) throw new ArgumentNullException(nameof(maximum));
            EnsureFinite(minimum);
            float initialMaximum = maximum.Read();
            EnsureFinite(initialMaximum);
            if (minimum > initialMaximum) throw new ArgumentOutOfRangeException(nameof(minimum));

            boundsDependency = StatsPropagationCoordinator.AddDependencies(
                maximum.DependencyNode == null ? Array.Empty<object>() : new[] { maximum.DependencyNode },
                this);

            try
            {
                StatsPropagationCoordinator.Execute(() =>
                {
                    this.minimum = minimum;
                    this.maximum = initialMaximum;
                    boundPolicy = policy;
                    maximumInput = maximum;
                    hasBounds = true;
                    hasMaximum = true;
                    ApplyDeclaredBoundsCore();
                    maximumSubscription = maximum.Subscribe(UpdateMaximum);
                });
            }
            catch
            {
                boundsDependency.Dispose();
                boundsDependency = null;
                throw;
            }
            return this;
        }

        public Resource WithBounds(ValueInput<float> minimum, ValueInput<float> maximum, ResourceBoundPolicy policy = ResourceBoundPolicy.Clamp)
        {
            threadGuard.Verify();
            EnsureBoundsAreNotDeclared();
            if (minimum == null) throw new ArgumentNullException(nameof(minimum));
            if (maximum == null) throw new ArgumentNullException(nameof(maximum));
            float initialMinimum = minimum.Read();
            float initialMaximum = maximum.Read();
            EnsureFinite(initialMinimum);
            EnsureFinite(initialMaximum);
            if (initialMinimum > initialMaximum) throw new ArgumentOutOfRangeException(nameof(minimum));
            var nodes = new System.Collections.Generic.List<object>();
            if (minimum.DependencyNode != null) nodes.Add(minimum.DependencyNode);
            if (maximum.DependencyNode != null) nodes.Add(maximum.DependencyNode);
            boundsDependency = StatsPropagationCoordinator.AddDependencies(nodes, this);
            try
            {
                StatsPropagationCoordinator.Execute(() =>
                {
                    this.minimum = initialMinimum;
                    this.maximum = initialMaximum;
                    minimumInput = minimum;
                    maximumInput = maximum;
                    boundPolicy = policy;
                    hasBounds = true;
                    hasMaximum = true;
                    ApplyDeclaredBoundsCore();
                    minimumSubscription = minimum.Subscribe(UpdateDynamicBounds);
                    maximumSubscription = maximum.Subscribe(UpdateDynamicBounds);
                });
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
            float previousMinimum = minimum;
            float previousMaximum = maximum;
            float nextMinimum = minimumInput.Read();
            float nextMaximum = maximumInput.Read();
            EnsureFinite(nextMinimum);
            EnsureFinite(nextMaximum);
            if (nextMinimum > nextMaximum) throw new InvalidOperationException("The dynamic minimum cannot exceed the maximum.");
            minimum = nextMinimum;
            maximum = nextMaximum;
            if (boundPolicy == ResourceBoundPolicy.PreserveRatio && previousMaximum != previousMinimum)
            {
                float ratio = (value - previousMinimum) / (previousMaximum - previousMinimum);
                Set(nextMinimum + ((nextMaximum - nextMinimum) * ratio));
            }
            else
            {
                Set(value);
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

        private void UpdateMaximum()
        {
            float previousMaximum = maximum;
            float nextMaximum = maximumInput.Read();
            EnsureFinite(nextMaximum);
            if (minimum > nextMaximum) throw new InvalidOperationException("The dynamic maximum cannot be below the minimum.");
            if (nextMaximum == previousMaximum) return;

            maximum = nextMaximum;
            if (boundPolicy == ResourceBoundPolicy.PreserveRatio && previousMaximum != minimum)
            {
                float ratio = (value - minimum) / (previousMaximum - minimum);
                Set(minimum + ((nextMaximum - minimum) * ratio));
                return;
            }

            Set(value);
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
            if (!hasBounds)
            {
                return value;
            }

            value = Math.Max(value, minimum);
            return hasMaximum ? Math.Min(value, maximum) : value;
        }

        private void EnsureBoundsAreNotDeclared()
        {
            if (hasBounds)
            {
                throw new InvalidOperationException("The Resource bounds have already been declared.");
            }
        }

        internal void VerifyThread() => threadGuard.Verify();
    }
}
