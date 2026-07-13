using System;

namespace Klrpxy.Gameplay.Stats
{
    public sealed class StatKey<TStat>
        where TStat : class
    {
        private readonly Type declaringType;
        private readonly Func<StatSet, TStat> getStat;
        private readonly string path;

        internal StatKey(
            Type declaringType,
            string path,
            Func<StatSet, TStat> getStat)
        {
            this.declaringType = declaringType;
            this.path = path;
            this.getStat = getStat;
        }

        public string GetPath() => path;

        public bool TryGet(StatSet statSet, out TStat stat)
        {
            if (statSet == null || !declaringType.IsInstanceOfType(statSet))
            {
                stat = null;
                return false;
            }

            stat = getStat(statSet);
            return true;
        }
    }
}
