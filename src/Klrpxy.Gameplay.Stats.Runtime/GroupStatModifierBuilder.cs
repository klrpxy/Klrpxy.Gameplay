using System;
using Klrpxy.Gameplay.Tags.Runtime;

namespace Klrpxy.Gameplay.Stats
{
    public sealed class GroupStatModifierBuilder
    {
        private readonly WeakReference source;
        private readonly WeakReference group;
        private readonly ITagQuery condition;
        private readonly StatKey<Stat> key;

        internal GroupStatModifierBuilder(WeakReference source, WeakReference group, ITagQuery condition, StatKey<Stat> key)
        {
            this.source = source;
            this.group = group;
            this.condition = condition;
            this.key = key;
        }

        public ModifierHandle Add(float value) => Register(Modifier.Flat(value, key));

        public ModifierHandle AddPercent(float value) => Register(Modifier.Percent(value, key));

        public ModifierHandle Multiply(float value) => Register(Modifier.Multiply(value, key));

        public ModifierHandle Override(float value, int priority = 0) => Register(Modifier.Override(value, key, priority));

        public ModifierHandle Clamp(float minimum, float maximum) => Register(Modifier.Clamp(minimum, maximum, key));

        private ModifierHandle Register(Modifier modifier)
        {
            var modifierSource = source.Target as ModifierSource;
            if (modifierSource == null) throw new ObjectDisposedException(nameof(ModifierSource));
            var targetGroup = group.Target as StatSubjectGroup;
            if (targetGroup == null) throw new ObjectDisposedException(nameof(StatSubjectGroup));
            if (condition != null) modifier = modifier.WhenTargetMatches(condition);
            return targetGroup.AddModifier(modifier, modifierSource);
        }
    }
}
