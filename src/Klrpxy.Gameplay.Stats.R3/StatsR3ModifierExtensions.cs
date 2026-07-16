using System;
using global::R3;

namespace Klrpxy.Gameplay.Stats.R3
{
    public static class StatsR3ModifierExtensions
    {
        public static ModifierHandle Add(
            this StatModifierBuilder builder,
            global::R3.ReadOnlyReactiveProperty<float> value)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            return builder.Add(R3ValueInput.Create(value));
        }

        public static ModifierHandle AddPercent(
            this StatModifierBuilder builder,
            global::R3.ReadOnlyReactiveProperty<float> value)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            return builder.AddPercent(R3ValueInput.Create(value));
        }

        public static ModifierHandle Multiply(
            this StatModifierBuilder builder,
            global::R3.ReadOnlyReactiveProperty<float> value)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            return builder.Multiply(R3ValueInput.Create(value));
        }

        public static ModifierHandle Override(
            this StatModifierBuilder builder,
            global::R3.ReadOnlyReactiveProperty<float> value,
            int priority = 0)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            return builder.Override(R3ValueInput.Create(value), priority);
        }

        public static R3StatModifierBuilder Where(
            this StatModifierBuilder builder,
            global::R3.ReadOnlyReactiveProperty<bool> condition)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            return new R3StatModifierBuilder(builder.Where(R3ValueInput.CreateCondition(condition)));
        }

        public static ModifierHandle Add(
            this GroupStatModifierBuilder builder,
            global::R3.ReadOnlyReactiveProperty<float> value)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            return builder.Add(R3ValueInput.Create(value));
        }

        public static ModifierHandle AddPercent(
            this GroupStatModifierBuilder builder,
            global::R3.ReadOnlyReactiveProperty<float> value)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            return builder.AddPercent(R3ValueInput.Create(value));
        }

        public static ModifierHandle Multiply(
            this GroupStatModifierBuilder builder,
            global::R3.ReadOnlyReactiveProperty<float> value)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            return builder.Multiply(R3ValueInput.Create(value));
        }

        public static ModifierHandle Override(
            this GroupStatModifierBuilder builder,
            global::R3.ReadOnlyReactiveProperty<float> value,
            int priority = 0)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            return builder.Override(R3ValueInput.Create(value), priority);
        }

        public static ModifierHandle Add(
            this GroupRangeStatModifierBuilder builder,
            global::R3.ReadOnlyReactiveProperty<float> value)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            return builder.Add(R3ValueInput.Create(value));
        }

        public static ModifierHandle AddPercent(
            this GroupRangeStatModifierBuilder builder,
            global::R3.ReadOnlyReactiveProperty<float> value)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            return builder.AddPercent(R3ValueInput.Create(value));
        }

        public static ModifierHandle Multiply(
            this GroupRangeStatModifierBuilder builder,
            global::R3.ReadOnlyReactiveProperty<float> value)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            return builder.Multiply(R3ValueInput.Create(value));
        }

        public static R3GroupModifierScopeBuilder Where(
            this GroupModifierScopeBuilder builder,
            global::R3.ReadOnlyReactiveProperty<bool> condition)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            return new R3GroupModifierScopeBuilder(builder.Where(R3ValueInput.CreateCondition(condition)));
        }
    }

    public sealed class R3StatModifierBuilder
    {
        private readonly StatModifierBuilder builder;

        internal R3StatModifierBuilder(StatModifierBuilder builder) => this.builder = builder;

        public R3StatModifierBuilder Where(global::R3.ReadOnlyReactiveProperty<bool> condition) =>
            new R3StatModifierBuilder(builder.Where(R3ValueInput.CreateCondition(condition)));

        public ModifierHandle Add(float value) => builder.Add(value);

        public ModifierHandle Add(global::R3.ReadOnlyReactiveProperty<float> value) =>
            builder.Add(R3ValueInput.Create(value));

        public ModifierHandle AddPercent(float value) => builder.AddPercent(value);

        public ModifierHandle AddPercent(global::R3.ReadOnlyReactiveProperty<float> value) =>
            builder.AddPercent(R3ValueInput.Create(value));

        public ModifierHandle Multiply(float value) => builder.Multiply(value);

        public ModifierHandle Multiply(global::R3.ReadOnlyReactiveProperty<float> value) =>
            builder.Multiply(R3ValueInput.Create(value));

        public ModifierHandle Override(float value, int priority = 0) => builder.Override(value, priority);

        public ModifierHandle Override(
            global::R3.ReadOnlyReactiveProperty<float> value,
            int priority = 0) => builder.Override(R3ValueInput.Create(value), priority);

        public ModifierHandle Clamp(float minimum, float maximum) => builder.Clamp(minimum, maximum);
    }

    public sealed class R3GroupModifierScopeBuilder
    {
        private readonly GroupModifierScopeBuilder builder;

        internal R3GroupModifierScopeBuilder(GroupModifierScopeBuilder builder) => this.builder = builder;

        public R3GroupModifierScopeBuilder Where(global::R3.ReadOnlyReactiveProperty<bool> condition) =>
            new R3GroupModifierScopeBuilder(builder.Where(R3ValueInput.CreateCondition(condition)));

        public R3GroupModifierScopeBuilder WhereTargetMatches(
            global::Klrpxy.Gameplay.Tags.Runtime.ITagQuery query) =>
            new R3GroupModifierScopeBuilder(builder.WhereTargetMatches(query));

        public R3GroupModifierScopeBuilder WhereTargetHas(
            global::Klrpxy.Gameplay.Tags.Runtime.IGameplayTag tag) =>
            new R3GroupModifierScopeBuilder(builder.WhereTargetHas(tag));

        public R3GroupStatModifierBuilder Modify(StatKey<Stat> key) =>
            new R3GroupStatModifierBuilder(builder.Modify(key));

        public R3GroupRangeStatModifierBuilder Modify(StatKey<RangeStat> key) =>
            new R3GroupRangeStatModifierBuilder(builder.Modify(key));
    }

    public sealed class R3GroupStatModifierBuilder
    {
        private readonly GroupStatModifierBuilder builder;

        internal R3GroupStatModifierBuilder(GroupStatModifierBuilder builder) => this.builder = builder;

        public ModifierHandle Add(float value) => builder.Add(value);

        public ModifierHandle Add(global::R3.ReadOnlyReactiveProperty<float> value) =>
            builder.Add(R3ValueInput.Create(value));

        public ModifierHandle AddPercent(float value) => builder.AddPercent(value);

        public ModifierHandle AddPercent(global::R3.ReadOnlyReactiveProperty<float> value) =>
            builder.AddPercent(R3ValueInput.Create(value));

        public ModifierHandle Multiply(float value) => builder.Multiply(value);

        public ModifierHandle Multiply(global::R3.ReadOnlyReactiveProperty<float> value) =>
            builder.Multiply(R3ValueInput.Create(value));

        public ModifierHandle Override(float value, int priority = 0) => builder.Override(value, priority);

        public ModifierHandle Override(
            global::R3.ReadOnlyReactiveProperty<float> value,
            int priority = 0) => builder.Override(R3ValueInput.Create(value), priority);

        public ModifierHandle Clamp(float minimum, float maximum) => builder.Clamp(minimum, maximum);
    }

    public sealed class R3GroupRangeStatModifierBuilder
    {
        private readonly GroupRangeStatModifierBuilder builder;

        internal R3GroupRangeStatModifierBuilder(GroupRangeStatModifierBuilder builder) => this.builder = builder;

        public ModifierHandle Add(float value) => builder.Add(value);

        public ModifierHandle Add(global::R3.ReadOnlyReactiveProperty<float> value) =>
            builder.Add(R3ValueInput.Create(value));

        public ModifierHandle AddPercent(float value) => builder.AddPercent(value);

        public ModifierHandle AddPercent(global::R3.ReadOnlyReactiveProperty<float> value) =>
            builder.AddPercent(R3ValueInput.Create(value));

        public ModifierHandle Multiply(float value) => builder.Multiply(value);

        public ModifierHandle Multiply(global::R3.ReadOnlyReactiveProperty<float> value) =>
            builder.Multiply(R3ValueInput.Create(value));

        public ModifierHandle Override(FloatRange value, int priority = 0) => builder.Override(value, priority);

        public ModifierHandle Clamp(float minimum, float maximum) => builder.Clamp(minimum, maximum);
    }

    internal static class R3ValueInput
    {
        internal static ValueInput<T> Create<T>(global::R3.ReadOnlyReactiveProperty<T> value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return new ValueInput<T>(
                () => value.CurrentValue,
                callback => Subscribe(value, callback),
                null,
                () => { });
        }

        internal static ModifierCondition CreateCondition(
            global::R3.ReadOnlyReactiveProperty<bool> condition)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            return new ModifierCondition(
                () => condition.CurrentValue,
                callback => Subscribe(condition, callback));
        }

        private static IDisposable Subscribe<T>(
            global::R3.ReadOnlyReactiveProperty<T> value,
            Action callback)
        {
            bool initial = true;
            return value.Subscribe(
                ignored =>
                {
                    if (initial)
                    {
                        initial = false;
                        return;
                    }

                    callback();
                },
                result =>
                {
                    if (result.IsFailure) StatsDiagnostics.Report(result.Exception);
                });
        }
    }
}
