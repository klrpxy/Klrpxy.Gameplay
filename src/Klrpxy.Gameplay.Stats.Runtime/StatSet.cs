using System;
using System.Collections.Generic;

namespace Klrpxy.Gameplay.Stats
{
    public abstract class StatSet
    {
        private readonly List<object> boundMembers = new List<object>();

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

            var members = new List<StatMemberDescriptor>();
            AppendGeneratedMembers(members);
            var seenMembers = new HashSet<object>();
            foreach (StatMemberDescriptor member in members)
            {
                object value = member.GetMember(this);
                if (value == null)
                {
                    throw new InvalidOperationException("The StatSet member '" + member.Path + "' must not be null.");
                }

                VerifyMemberThread(value);

                if (!seenMembers.Add(value))
                {
                    throw new InvalidOperationException("The StatSet member '" + member.Path + "' duplicates another member instance.");
                }

                if (GetMemberStatSet(value) != null)
                {
                    throw new InvalidOperationException("The StatSet member '" + member.Path + "' already belongs to another StatSet.");
                }
            }

            foreach (StatMemberDescriptor member in members)
            {
                object value = member.GetMember(this);
                SetMemberStatSet(value, this);
                boundMembers.Add(value);
            }

            Owner = owner;
        }

        internal void DisposeMembers()
        {
            foreach (object member in boundMembers)
            {
                if (member is Stat stat) { stat.Dispose(); continue; }
                if (member is RangeStat rangeStat) { rangeStat.Dispose(); continue; }
                ((Resource)member).Dispose();
            }
        }

        private static StatSet GetMemberStatSet(object member)
        {
            var stat = member as Stat;
            if (stat != null) return stat.StatSet;
            var rangeStat = member as RangeStat;
            if (rangeStat != null) return rangeStat.StatSet;
            return ((Resource)member).StatSet;
        }

        private static void VerifyMemberThread(object member)
        {
            var stat = member as Stat;
            if (stat != null) { stat.VerifyThread(); return; }
            var rangeStat = member as RangeStat;
            if (rangeStat != null) { rangeStat.VerifyThread(); return; }
            ((Resource)member).VerifyThread();
        }

        private static void SetMemberStatSet(object member, StatSet statSet)
        {
            var stat = member as Stat;
            if (stat != null) { stat.StatSet = statSet; return; }
            var rangeStat = member as RangeStat;
            if (rangeStat != null) { rangeStat.StatSet = statSet; return; }
            ((Resource)member).StatSet = statSet;
        }
    }
}
