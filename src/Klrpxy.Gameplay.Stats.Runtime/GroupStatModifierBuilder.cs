using System;
using Klrpxy.Gameplay.Tags.Runtime;

namespace Klrpxy.Gameplay.Stats
{
    public sealed class GroupStatModifierBuilder
    {
        private readonly WeakReference source;
        private readonly WeakReference group;
        private readonly ITagQuery condition;
        private readonly ModifierCondition sharedCondition;
        private readonly StatKey<Stat> key;

        internal GroupStatModifierBuilder(
            WeakReference source,
            WeakReference group,
            ITagQuery condition,
            ModifierCondition sharedCondition,
            StatKey<Stat> key)
        {
            this.source = source;
            this.group = group;
            this.condition = condition;
            this.sharedCondition = sharedCondition;
            this.key = key;
        }

        public ModifierHandle Add(float value) => Register(Modifier.Flat(value, key));

        public ModifierHandle Add(Stat input, Func<float, float> selector) =>
            Register(Modifier.Flat(ModifierValue.FromResilient(ValueInput.Final(input), selector), key));

        public ModifierHandle Add(RangeStat input, Func<FloatRange, float> selector) =>
            Register(Modifier.Flat(ModifierValue.FromResilient(ValueInput.Final(input), selector), key));

        public ModifierHandle Add(Resource input, Func<float, float> selector) =>
            Register(Modifier.Flat(ModifierValue.FromResilient(ValueInput.Current(input), selector), key));

        internal ModifierHandle Add(ValueInput<float> input) =>
            Register(Modifier.Flat(ModifierValue.FromResilient(input, value => value), key));

        public ModifierHandle AddPercent(float value) => Register(Modifier.Percent(value, key));

        public ModifierHandle AddPercent(Stat input, Func<float, float> selector) =>
            Register(Modifier.Percent(ModifierValue.FromResilient(ValueInput.Final(input), selector), key));

        public ModifierHandle AddPercent(RangeStat input, Func<FloatRange, float> selector) =>
            Register(Modifier.Percent(ModifierValue.FromResilient(ValueInput.Final(input), selector), key));

        public ModifierHandle AddPercent(Resource input, Func<float, float> selector) =>
            Register(Modifier.Percent(ModifierValue.FromResilient(ValueInput.Current(input), selector), key));

        internal ModifierHandle AddPercent(ValueInput<float> input) =>
            Register(Modifier.Percent(ModifierValue.FromResilient(input, value => value), key));

        public ModifierHandle Multiply(float value) => Register(Modifier.Multiply(value, key));

        public ModifierHandle Multiply(Stat input, Func<float, float> selector) =>
            Register(Modifier.Multiply(ModifierValue.FromResilient(ValueInput.Final(input), selector), key));

        public ModifierHandle Multiply(RangeStat input, Func<FloatRange, float> selector) =>
            Register(Modifier.Multiply(ModifierValue.FromResilient(ValueInput.Final(input), selector), key));

        public ModifierHandle Multiply(Resource input, Func<float, float> selector) =>
            Register(Modifier.Multiply(ModifierValue.FromResilient(ValueInput.Current(input), selector), key));

        internal ModifierHandle Multiply(ValueInput<float> input) =>
            Register(Modifier.Multiply(ModifierValue.FromResilient(input, value => value), key));

        public ModifierHandle Override(float value, int priority = 0) => Register(Modifier.Override(value, key, priority));

        public ModifierHandle Override(Stat input, Func<float, float> selector, int priority = 0) =>
            Register(Modifier.Override(ModifierValue.FromResilient(ValueInput.Final(input), selector), key, priority));

        public ModifierHandle Override(RangeStat input, Func<FloatRange, float> selector, int priority = 0) =>
            Register(Modifier.Override(ModifierValue.FromResilient(ValueInput.Final(input), selector), key, priority));

        public ModifierHandle Override(Resource input, Func<float, float> selector, int priority = 0) =>
            Register(Modifier.Override(ModifierValue.FromResilient(ValueInput.Current(input), selector), key, priority));

        internal ModifierHandle Override(ValueInput<float> input, int priority) =>
            Register(Modifier.Override(ModifierValue.FromResilient(input, value => value), key, priority));

        public ModifierHandle Clamp(float minimum, float maximum) => Register(Modifier.Clamp(minimum, maximum, key));

        private ModifierHandle Register(Modifier modifier)
        {
            var modifierSource = source.Target as ModifierSource;
            if (modifierSource == null) throw new ObjectDisposedException(nameof(ModifierSource));
            var targetGroup = group.Target as StatSubjectGroup;
            if (targetGroup == null) throw new ObjectDisposedException(nameof(StatSubjectGroup));
            if (condition != null) modifier = modifier.WhenTargetMatches(condition);
            if (sharedCondition != null) modifier = modifier.When(sharedCondition);
            return targetGroup.AddModifier(modifier, modifierSource);
        }
    }
}
