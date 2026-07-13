using System;

namespace Klrpxy.Gameplay.Stats
{
    public abstract class StatsOwner
    {
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
