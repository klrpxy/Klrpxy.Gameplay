using System;

namespace Klrpxy.Gameplay.Stats
{
    public sealed class Modifier
    {
        private Modifier(ModifierKind kind, float value, FloatRange range, int priority, object target)
        {
            Kind = kind;
            Value = value;
            Range = range;
            Priority = priority;
            Target = target;
        }

        internal ModifierKind Kind { get; }

        internal float Value { get; }

        internal FloatRange Range { get; }

        internal int Priority { get; }

        internal object Target { get; }

        public static Modifier Flat(float value, StatKey<Stat> target) => Create(ModifierKind.Flat, value, target, 0);

        public static Modifier Flat(float value, StatKey<RangeStat> target) => Create(ModifierKind.Flat, value, target, 0);

        public static Modifier Percent(float value, StatKey<Stat> target) => Create(ModifierKind.Percent, value / 100f, target, 0);

        public static Modifier Percent(float value, StatKey<RangeStat> target) => Create(ModifierKind.Percent, value / 100f, target, 0);

        public static Modifier Multiply(float value, StatKey<Stat> target) => Create(ModifierKind.Multiply, value, target, 0);

        public static Modifier Multiply(float value, StatKey<RangeStat> target) => Create(ModifierKind.Multiply, value, target, 0);

        public static Modifier Override(float value, StatKey<Stat> target, int priority = 0) => Create(ModifierKind.Override, value, target, priority);

        public static Modifier Override(FloatRange value, StatKey<RangeStat> target, int priority = 0)
        {
            ValidateFinite(value.Min, nameof(value));
            ValidateFinite(value.Max, nameof(value));
            return new Modifier(ModifierKind.Override, 0f, value, priority, target ?? throw new ArgumentNullException(nameof(target)));
        }

        public static Modifier Clamp(float minimum, float maximum, StatKey<Stat> target, int priority = 0) => CreateClamp(minimum, maximum, target, priority);

        public static Modifier Clamp(float minimum, float maximum, StatKey<RangeStat> target, int priority = 0) => CreateClamp(minimum, maximum, target, priority);

        private static Modifier Create<TStat>(ModifierKind kind, float value, StatKey<TStat> target, int priority)
            where TStat : class
        {
            ValidateFinite(value, nameof(value));
            return new Modifier(kind, value, default(FloatRange), priority, target ?? throw new ArgumentNullException(nameof(target)));
        }

        private static Modifier CreateClamp<TStat>(float minimum, float maximum, StatKey<TStat> target, int priority)
            where TStat : class
        {
            FloatRange range = CreateClampRange(minimum, maximum);
            return new Modifier(ModifierKind.Clamp, 0f, range, priority, target ?? throw new ArgumentNullException(nameof(target)));
        }

        private static FloatRange CreateClampRange(float minimum, float maximum)
        {
            ValidateFinite(minimum, nameof(minimum));
            ValidateFinite(maximum, nameof(maximum));
            if (minimum > maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(minimum));
            }

            return new FloatRange(minimum, maximum);
        }

        internal static void ValidateFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }

    internal enum ModifierKind
    {
        Flat,
        Percent,
        Multiply,
        Override,
        Clamp
    }
}
