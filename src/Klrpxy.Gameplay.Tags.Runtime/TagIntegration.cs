using System;
using System.Collections.Generic;

namespace Klrpxy.Gameplay.Tags.Runtime
{
    public interface IGameplayTag
    {
    }

    public interface IHierarchicalGameplayTag : IGameplayTag
    {
        bool IsSameOrDescendantOf(IGameplayTag tag);
    }

    public sealed class TagSetChange
    {
        public TagSetChange(IGameplayTag tag, TagSetChangeKind kind)
        {
            Tag = tag;
            Kind = kind;
        }

        public IGameplayTag Tag { get; }

        public TagSetChangeKind Kind { get; }
    }

    public interface ITagSet
    {
        event Action<TagSetChange> OnChanged;

        bool Add(IGameplayTag tag);

        bool Remove(IGameplayTag tag);

        IEnumerable<IGameplayTag> Values { get; }
    }

    public interface ITagQuery
    {
        bool Matches(ITagSet tags);
    }
}
