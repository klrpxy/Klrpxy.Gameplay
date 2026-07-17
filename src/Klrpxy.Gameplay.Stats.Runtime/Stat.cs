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
        private float? minimumBound;
        private float? maximumBound;
        private float finalValue;
        private readonly GameplayThreadGuard threadGuard = new GameplayThreadGuard();
        private ValueInput<float> minimumInput;
        private ValueInput<float> maximumInput;
        private IDisposable minimumSubscription;
        private IDisposable maximumSubscription;
        private IDisposable minimumDependency;
        private IDisposable maximumDependency;
        private bool disposed;

        public Stat(float baseValue, RoundingMode rounding = RoundingMode.None)
        {
            this.rounding = rounding;
            BaseValue = baseValue;
        }

        public float BaseValue
        {
            get
            {
                ThrowIfDisposed();
                return baseValue;
            }
            set
            {
                StatsPropagationCoordinator.Execute(() =>
                {
                    threadGuard.Verify();
                    ThrowIfDisposed();
                    Modifier.ValidateFinite(value, nameof(value));
                    if (baseValue == value) return;
                    float previous = baseValue;
                    baseValue = value;
                    RecalculateCore();
                    OnBaseValueChanged?.Invoke(previous, value);
                });
            }
        }

        public float FinalValue
        {
            get
            {
                ThrowIfDisposed();
                return finalValue;
            }
        }

        public event Action<float, float> OnFinalValueChanged;

        internal event Action<float, float> OnBaseValueChanged;

        internal event Action FinalValueChanged;

        public Stat WithMinimum(float minimum)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            Modifier.ValidateFinite(minimum, nameof(minimum));
            float rounded = rounding == RoundingMode.None ? minimum : (float)Math.Ceiling(minimum);
            float? nextMaximum = ReadCurrentMaximum();
            if (nextMaximum.HasValue && rounded > nextMaximum.Value)
            {
                throw new ArgumentOutOfRangeException(nameof(minimum));
            }

            IDisposable previousSubscription = minimumSubscription;
            IDisposable previousDependency = minimumDependency;
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

        public Stat WithMaximum(float maximum)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            Modifier.ValidateFinite(maximum, nameof(maximum));
            float rounded = rounding == RoundingMode.None ? maximum : (float)Math.Floor(maximum);
            float? nextMinimum = ReadCurrentMinimum();
            if (nextMinimum.HasValue && nextMinimum.Value > rounded)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum));
            }

            IDisposable previousSubscription = maximumSubscription;
            IDisposable previousDependency = maximumDependency;
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

        public Stat WithMinimum(ValueInput<float> minimum)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            if (minimum == null) throw new ArgumentNullException(nameof(minimum));
            float nextMinimum = RoundMinimum(minimum.Read());
            float? nextMaximum = ReadCurrentMaximum();
            if (nextMaximum.HasValue && nextMinimum > nextMaximum.Value)
            {
                throw new ArgumentOutOfRangeException(nameof(minimum));
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

        public Stat WithMaximum(ValueInput<float> maximum)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            if (maximum == null) throw new ArgumentNullException(nameof(maximum));
            float nextMaximum = RoundMaximum(maximum.Read());
            float? nextMinimum = ReadCurrentMinimum();
            if (nextMinimum.HasValue && nextMinimum.Value > nextMaximum)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum));
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
                throw new InvalidOperationException("The dynamic minimum cannot exceed the maximum.");
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
            return rounding == RoundingMode.None ? minimum : (float)Math.Ceiling(minimum);
        }

        private float RoundMaximum(float maximum)
        {
            Modifier.ValidateFinite(maximum, nameof(maximum));
            return rounding == RoundingMode.None ? maximum : (float)Math.Floor(maximum);
        }

        internal ModifierHandle AddModifier(Modifier modifier, ModifierSource source, long order)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            var registration = new ModifierRegistration(modifier, order);
            ModifierHandle handle = null;
            handle = new ModifierHandle(source, ignored =>
            {
                modifiers.Remove(registration);
                registration.Dispose();
            }, Recalculate);
            bool sourceAdded = false;
            bool modifierAdded = false;
            try
            {
                registration.Subscribe(Recalculate, this);
                source.Add(handle);
                sourceAdded = true;
                modifiers.Add(registration);
                modifierAdded = true;
                Recalculate();
                return handle;
            }
            catch
            {
                if (modifierAdded) modifiers.Remove(registration);
                registration.Dispose();
                if (sourceAdded) source.Remove(handle);
                throw;
            }
        }

        internal void AddConditionalRegistration(ModifierRegistration registration)
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

        internal void RemoveConditionalRegistration(ModifierRegistration registration)
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
            foreach (ModifierRegistration registration in modifiers) registration.Dispose();
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
            OnBaseValueChanged = null;
            FinalValueChanged = null;
            OnFinalValueChanged = null;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(Stat));
        }

        private void RecalculateCore()
        {
            float previous = finalValue;
            List<IModifierEntry> allModifiers = GetAllModifiers();
            float calculated = ModifierCalculation.CalculateArithmetic(baseValue, allModifiers);

            IModifierEntry overrideRegistration = SelectWinning(allModifiers, ModifierKind.Override);
            if (overrideRegistration != null)
            {
                calculated = overrideRegistration.Modifier.Value;
            }

            calculated = ModifierCalculation.Round(calculated, rounding);
            FloatRange? clamp = CombineClamps(allModifiers);
            if (clamp.HasValue)
            {
                calculated = Clamp(calculated, clamp.Value);
            }

            if (minimumBound.HasValue)
            {
                calculated = Math.Max(calculated, minimumBound.Value);
            }

            if (maximumBound.HasValue)
            {
                calculated = Math.Min(calculated, maximumBound.Value);
            }

            Modifier.ValidateFinite(calculated, "calculation");
            finalValue = calculated;
            if (previous != finalValue)
            {
                FinalValueChanged?.Invoke();
                StatsPropagationCoordinator.RecordChange(this, () => OnFinalValueChanged, previous, finalValue);
            }
        }

        private List<IModifierEntry> GetAllModifiers()
        {
            var result = new List<IModifierEntry>();
            foreach (ModifierRegistration modifier in modifiers) result.Add(modifier);
            StatSet?.Subject?.AppendGroupModifiers(this, result);
            result.Sort((left, right) => left.Order.CompareTo(right.Order));
            return result;
        }

        internal static IModifierEntry SelectWinning(List<IModifierEntry> registrations, ModifierKind kind)
        {
            IModifierEntry result = null;
            foreach (IModifierEntry registration in registrations)
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

        internal static FloatRange? CombineClamps(List<IModifierEntry> registrations)
        {
            var clamps = new List<IModifierEntry>();
            foreach (IModifierEntry registration in registrations)
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
            foreach (IModifierEntry clamp in clamps)
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

        internal sealed class ModifierRegistration : IDisposable, IModifierEntry
        {
            private IDisposable subscription;
            private IDisposable dependencyRegistration;
            public ModifierRegistration(Modifier modifier, long order)
            {
                Modifier = modifier;
                Order = order;
            }

            public Modifier Modifier { get; }

            public long Order { get; }

            internal void Subscribe(Action recalculate, object target)
            {
                if (Modifier.DynamicValue == null) return;
                dependencyRegistration = StatsPropagationCoordinator.AddDependencies(Modifier.DynamicValue.DependencyNodes, target);
                try
                {
                    subscription = Modifier.DynamicValue.Subscribe(recalculate);
                }
                catch
                {
                    dependencyRegistration.Dispose();
                    dependencyRegistration = null;
                    throw;
                }
            }

            public void Dispose()
            {
                subscription?.Dispose();
                dependencyRegistration?.Dispose();
                subscription = null;
                dependencyRegistration = null;
            }
        }
    }
}
