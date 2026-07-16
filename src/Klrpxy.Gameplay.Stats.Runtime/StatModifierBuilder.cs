using System;

namespace Klrpxy.Gameplay.Stats
{
    public sealed class StatModifierBuilder
    {
        private readonly WeakReference source;
        private readonly WeakReference stat;
        private readonly ModifierCondition condition;

        internal StatModifierBuilder(ModifierSource source, Stat stat)
            : this(new WeakReference(source), new WeakReference(stat), null)
        {
        }

        private StatModifierBuilder(WeakReference source, WeakReference stat, ModifierCondition condition)
        {
            this.source = source;
            this.stat = stat;
            this.condition = condition;
        }

        internal StatModifierBuilder Where(ModifierCondition next) =>
            new StatModifierBuilder(source, stat, ModifierCondition.Combine(condition, next));

        public ModifierHandle Add(float value) => Register(Modifier.CreateDirectFlat(value));

        public ModifierHandle Add(Stat input, Func<float, float> selector) =>
            Register(Modifier.CreateDirectFlat(ModifierValue.FromResilient(ValueInput.Final(input), selector)));

        public ModifierHandle Add(RangeStat input, Func<FloatRange, float> selector) =>
            Register(Modifier.CreateDirectFlat(ModifierValue.FromResilient(ValueInput.Final(input), selector)));

        public ModifierHandle Add(Resource input, Func<float, float> selector) =>
            Register(Modifier.CreateDirectFlat(ModifierValue.FromResilient(ValueInput.Current(input), selector)));

        internal ModifierHandle Add(ValueInput<float> input) =>
            Register(Modifier.CreateDirectFlat(ModifierValue.FromResilient(input, value => value)));

        public ModifierHandle AddPercent(float value) => Register(Modifier.CreateDirectPercent(value));

        public ModifierHandle AddPercent(Stat input, Func<float, float> selector) =>
            Register(Modifier.CreateDirectPercent(ModifierValue.FromResilient(ValueInput.Final(input), selector)));

        public ModifierHandle AddPercent(RangeStat input, Func<FloatRange, float> selector) =>
            Register(Modifier.CreateDirectPercent(ModifierValue.FromResilient(ValueInput.Final(input), selector)));

        public ModifierHandle AddPercent(Resource input, Func<float, float> selector) =>
            Register(Modifier.CreateDirectPercent(ModifierValue.FromResilient(ValueInput.Current(input), selector)));

        internal ModifierHandle AddPercent(ValueInput<float> input) =>
            Register(Modifier.CreateDirectPercent(ModifierValue.FromResilient(input, value => value)));

        public ModifierHandle Multiply(float value) => Register(Modifier.CreateDirectMultiply(value));

        public ModifierHandle Multiply(Stat input, Func<float, float> selector) =>
            Register(Modifier.CreateDirectMultiply(ModifierValue.FromResilient(ValueInput.Final(input), selector)));

        public ModifierHandle Multiply(RangeStat input, Func<FloatRange, float> selector) =>
            Register(Modifier.CreateDirectMultiply(ModifierValue.FromResilient(ValueInput.Final(input), selector)));

        public ModifierHandle Multiply(Resource input, Func<float, float> selector) =>
            Register(Modifier.CreateDirectMultiply(ModifierValue.FromResilient(ValueInput.Current(input), selector)));

        internal ModifierHandle Multiply(ValueInput<float> input) =>
            Register(Modifier.CreateDirectMultiply(ModifierValue.FromResilient(input, value => value)));

        public ModifierHandle Override(float value, int priority = 0) => Register(Modifier.CreateDirectOverride(value, priority));

        public ModifierHandle Override(Stat input, Func<float, float> selector, int priority = 0) =>
            Register(Modifier.CreateDirectOverride(ModifierValue.FromResilient(ValueInput.Final(input), selector), priority));

        public ModifierHandle Override(RangeStat input, Func<FloatRange, float> selector, int priority = 0) =>
            Register(Modifier.CreateDirectOverride(ModifierValue.FromResilient(ValueInput.Final(input), selector), priority));

        public ModifierHandle Override(Resource input, Func<float, float> selector, int priority = 0) =>
            Register(Modifier.CreateDirectOverride(ModifierValue.FromResilient(ValueInput.Current(input), selector), priority));

        internal ModifierHandle Override(ValueInput<float> input, int priority) =>
            Register(Modifier.CreateDirectOverride(ModifierValue.FromResilient(input, value => value), priority));

        public ModifierHandle Clamp(float minimum, float maximum) => Register(Modifier.CreateDirectClamp(minimum, maximum));

        private ModifierHandle Register(Modifier modifier)
        {
            if (condition != null) modifier = modifier.When(condition);
            var target = stat.Target as Stat;
            if (target == null)
            {
                throw new ArgumentNullException("stat");
            }

            var modifierSource = source.Target as ModifierSource;
            if (modifierSource == null)
            {
                throw new ObjectDisposedException(nameof(ModifierSource));
            }

            modifierSource.ThrowIfDisposed();
            target.VerifyThread();
            StatSubject subject = target.StatSet?.Subject;
            if (subject == null)
            {
                throw new InvalidOperationException("The Stat must belong to a StatSubject before it can be modified.");
            }

            return subject.AddDirectModifier(modifier, target, modifierSource);
        }
    }
}
