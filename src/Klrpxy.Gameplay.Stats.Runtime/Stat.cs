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
        private readonly GameplayThreadGuard threadGuard = new GameplayThreadGuard();
        private ValueInput<float> minimumInput;
        private ValueInput<float> maximumInput;
        private IDisposable minimumSubscription;
        private IDisposable maximumSubscription;
        private IDisposable boundsDependency;
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

        public Stat WithBounds(float minimum, float maximum)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
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

        public Stat WithBounds(ValueInput<float> minimum, ValueInput<float> maximum)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            if (minimum == null) throw new ArgumentNullException(nameof(minimum));
            if (maximum == null) throw new ArgumentNullException(nameof(maximum));
            FloatRange initialBounds = CreateBounds(minimum.Read(), maximum.Read());
            boundsDependency = StatsPropagationCoordinator.AddDependencies(GetDependencyNodes(minimum, maximum), this);
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
            if (minimum > maximum) throw new ArgumentOutOfRangeException(nameof(minimum));
            var result = new FloatRange(
                rounding == RoundingMode.None ? minimum : (float)Math.Ceiling(minimum),
                rounding == RoundingMode.None ? maximum : (float)Math.Floor(maximum));
            if (result.Min > result.Max) throw new ArgumentOutOfRangeException(nameof(minimum));
            return result;
        }

        private static IEnumerable<object> GetDependencyNodes(ValueInput<float> minimum, ValueInput<float> maximum)
        {
            if (minimum.DependencyNode != null) yield return minimum.DependencyNode;
            if (maximum.DependencyNode != null) yield return maximum.DependencyNode;
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
            registration.Subscribe(Recalculate, this);
            source.Add(handle);
            modifiers.Add(registration);
            Recalculate();
            return handle;
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
            boundsDependency?.Dispose();
            StatsPropagationCoordinator.RemoveNode(this);
            minimumSubscription = null;
            maximumSubscription = null;
            boundsDependency = null;
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

            if (bounds.HasValue)
            {
                calculated = Clamp(calculated, bounds.Value);
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
            StatSet?.Owner?.AppendGroupModifiers(this, result);
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
