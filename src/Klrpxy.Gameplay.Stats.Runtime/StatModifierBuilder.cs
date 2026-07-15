using System;

namespace Klrpxy.Gameplay.Stats
{
    public sealed class StatModifierBuilder
    {
        private readonly WeakReference source;
        private readonly WeakReference stat;

        internal StatModifierBuilder(ModifierSource source, Stat stat)
        {
            this.source = new WeakReference(source);
            this.stat = new WeakReference(stat);
        }

        public ModifierHandle Add(float value) => Register(Modifier.CreateDirectFlat(value));

        public ModifierHandle AddPercent(float value) => Register(Modifier.CreateDirectPercent(value));

        public ModifierHandle Multiply(float value) => Register(Modifier.CreateDirectMultiply(value));

        public ModifierHandle Override(float value, int priority = 0) => Register(Modifier.CreateDirectOverride(value, priority));

        public ModifierHandle Clamp(float minimum, float maximum) => Register(Modifier.CreateDirectClamp(minimum, maximum));

        private ModifierHandle Register(Modifier modifier)
        {
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
