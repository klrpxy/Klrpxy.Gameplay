using System;
using System.Threading;

namespace Klrpxy.Gameplay.Stats
{
    public abstract class StatsOwner
    {
        private static long nextModifierOrder;
        private readonly GameplayThreadGuard threadGuard = new GameplayThreadGuard();
        protected StatsOwner(StatSet statSet)
        {
            if (statSet == null)
            {
                throw new ArgumentNullException(nameof(statSet));
            }

            StatSet = statSet;
            statSet.Bind(this);
        }

        public StatSet StatSet { get; }

        public ModifierHandle AddModifier(Modifier modifier, ModifierSource source)
        {
            threadGuard.Verify();
            if (modifier == null)
            {
                throw new ArgumentNullException(nameof(modifier));
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            source.ThrowIfDisposed();
            long order = Interlocked.Increment(ref nextModifierOrder);
            if (modifier.Target is StatKey<Stat> statKey && statKey.TryGet(StatSet, out Stat stat))
            {
                return stat.AddModifier(modifier, source, order);
            }

            if (modifier.Target is StatKey<RangeStat> rangeKey && rangeKey.TryGet(StatSet, out RangeStat rangeStat))
            {
                return rangeStat.AddModifier(modifier, source, order);
            }

            throw new InvalidOperationException("The Modifier target is not declared by this StatsOwner.");
        }
    }

    public abstract class StatsOwner<TStatSet> : StatsOwner
        where TStatSet : StatSet
    {
        protected StatsOwner(TStatSet statSet)
            : base(statSet)
        {
        }

        public new TStatSet StatSet => (TStatSet)base.StatSet;
    }
}
