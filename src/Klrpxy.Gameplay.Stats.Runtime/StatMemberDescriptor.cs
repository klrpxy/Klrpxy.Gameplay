using System;

namespace Klrpxy.Gameplay.Stats
{
    public enum StatMemberKind
    {
        Stat,
        RangeStat,
        Resource
    }

    public sealed class StatMemberDescriptor
    {
        private readonly Func<StatSet, object> getMember;

        internal StatMemberDescriptor(
            string path,
            StatMemberKind kind,
            Type memberType,
            Func<StatSet, object> getMember)
        {
            Path = path;
            Kind = kind;
            MemberType = memberType;
            this.getMember = getMember;
        }

        public string Path { get; }

        public StatMemberKind Kind { get; }

        public Type MemberType { get; }

        internal object GetMember(StatSet statSet) => getMember(statSet);
    }
}
