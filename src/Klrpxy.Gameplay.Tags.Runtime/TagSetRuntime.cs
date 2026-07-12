using System;
using System.Collections.Generic;

namespace Klrpxy.Gameplay.Tags.Runtime
{
    public sealed class TagSetRuntime<TTag>
    {
        private readonly HashSet<TTag> tags = new HashSet<TTag>();

        public bool Add(TTag tag)
        {
            if (ReferenceEquals(tag, null))
            {
                throw new ArgumentNullException(nameof(tag));
            }

            return tags.Add(tag);
        }

        public bool Remove(TTag tag) => tags.Remove(tag);

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
    }
}
