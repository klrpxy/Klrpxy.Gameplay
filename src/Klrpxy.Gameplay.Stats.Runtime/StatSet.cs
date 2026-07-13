using System;
using System.Collections.Generic;

namespace Klrpxy.Gameplay.Stats
{
    public abstract class StatSet
    {
        public StatsOwner Owner { get; private set; }

        protected static StatKey<TStat> CreateKey<TStat>(
            Type declaringType,
            string path,
            Func<StatSet, TStat> getStat)
            where TStat : class
        {
            return new StatKey<TStat>(declaringType, path, getStat);
        }

        protected static StatMemberDescriptor CreateMember<TMember>(
            string path,
            StatMemberKind kind,
            Func<StatSet, TMember> getMember)
            where TMember : class
        {
            return new StatMemberDescriptor(
                path,
                kind,
                typeof(TMember),
                statSet => getMember(statSet));
        }

        protected virtual void AppendGeneratedMembers(ICollection<StatMemberDescriptor> members)
        {
        }

        internal void Bind(StatsOwner owner)
        {
            if (Owner != null)
            {
                throw new InvalidOperationException("The StatSet already belongs to a StatsOwner.");
            }

            Owner = owner;
        }
    }
}
