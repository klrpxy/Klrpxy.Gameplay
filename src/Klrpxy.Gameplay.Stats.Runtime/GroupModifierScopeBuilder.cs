using System;
using Klrpxy.Gameplay.Tags.Runtime;

namespace Klrpxy.Gameplay.Stats
{
    public sealed class GroupModifierScopeBuilder
    {
        private readonly WeakReference source;
        private readonly WeakReference group;
        private readonly ITagQuery condition;
        private readonly ModifierCondition sharedCondition;

        internal GroupModifierScopeBuilder(ModifierSource source, StatSubjectGroup group)
            : this(new WeakReference(source), new WeakReference(group), null, null)
        {
        }

        private GroupModifierScopeBuilder(
            WeakReference source,
            WeakReference group,
            ITagQuery condition,
            ModifierCondition sharedCondition)
        {
            this.source = source;
            this.group = group;
            this.condition = condition;
            this.sharedCondition = sharedCondition;
        }

        public GroupStatModifierBuilder Modify(StatKey<Stat> key) =>
            new GroupStatModifierBuilder(source, group, condition, sharedCondition, key);

        public GroupRangeStatModifierBuilder Modify(StatKey<RangeStat> key) =>
            new GroupRangeStatModifierBuilder(source, group, condition, sharedCondition, key);

        internal GroupModifierScopeBuilder Where(ModifierCondition next) =>
            new GroupModifierScopeBuilder(
                source,
                group,
                condition,
                ModifierCondition.Combine(sharedCondition, next));

        public GroupModifierScopeBuilder WhereTargetMatches(ITagQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            return new GroupModifierScopeBuilder(
                source,
                group,
                TagQueryConjunction.Combine(condition, query),
                sharedCondition);
        }

        public GroupModifierScopeBuilder WhereTargetHas(IGameplayTag tag)
        {
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            return WhereTargetMatches(new HasTagQuery(tag));
        }

        private sealed class HasTagQuery : ITagQuery
        {
            private readonly IGameplayTag tag;

            internal HasTagQuery(IGameplayTag tag) => this.tag = tag;

            public bool Matches(ITagSet tags)
            {
                foreach (IGameplayTag candidate in tags.Values)
                {
                    var hierarchical = candidate as IHierarchicalGameplayTag;
                    if (hierarchical != null
                        ? hierarchical.IsSameOrDescendantOf(tag)
                        : ReferenceEquals(candidate, tag))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    internal sealed class TagQueryConjunction : ITagQuery
    {
        private readonly ITagQuery first;
        private readonly ITagQuery second;

        private TagQueryConjunction(ITagQuery first, ITagQuery second)
        {
            this.first = first;
            this.second = second;
        }

        internal static ITagQuery Combine(ITagQuery first, ITagQuery second) =>
            first == null ? second : new TagQueryConjunction(first, second);

        public bool Matches(ITagSet tags) => first.Matches(tags) && second.Matches(tags);
    }
}
