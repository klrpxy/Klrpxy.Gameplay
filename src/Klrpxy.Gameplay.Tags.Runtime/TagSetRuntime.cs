using System;
using System.Collections.Generic;

namespace Klrpxy.Gameplay.Tags.Runtime
{
    public enum TagSetChangeKind
    {
        Added,
        Removed
    }

    public sealed class TagSetChange<TTag>
    {
        public TagSetChange(TTag tag, TagSetChangeKind kind)
        {
            Tag = tag;
            Kind = kind;
        }

        public TTag Tag { get; }

        public TagSetChangeKind Kind { get; }
    }

    public sealed class TagSetRuntime<TTag>
    {
        private readonly HashSet<TTag> tags = new HashSet<TTag>();

        public event Action<TagSetChange<TTag>> OnChanged;

        public bool Add(TTag tag)
        {
            if (ReferenceEquals(tag, null))
            {
                throw new ArgumentNullException(nameof(tag));
            }

            if (!tags.Add(tag))
            {
                return false;
            }

            OnChanged?.Invoke(new TagSetChange<TTag>(tag, TagSetChangeKind.Added));
            return true;
        }

        public bool Remove(TTag tag)
        {
            if (!tags.Remove(tag))
            {
                return false;
            }

            OnChanged?.Invoke(new TagSetChange<TTag>(tag, TagSetChangeKind.Removed));
            return true;
        }

        public bool HasExact(TTag tag) => tags.Contains(tag);

        public bool Has(TTag tag, Func<TTag, TTag, bool> isSameOrDescendant)
        {
            foreach (TTag ownedTag in tags)
            {
                if (isSameOrDescendant(ownedTag, tag))
                {
                    return true;
                }
            }

            return false;
        }

        public IEnumerable<TTag> Values => new List<TTag>(tags);
    }
}
