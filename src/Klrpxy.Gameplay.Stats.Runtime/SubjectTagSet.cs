using System;
using System.Collections.Generic;
using Klrpxy.Gameplay.Tags.Runtime;

namespace Klrpxy.Gameplay.Stats
{
    internal sealed class SubjectTagSet : ITagSet
    {
        private readonly HashSet<IGameplayTag> tags = new HashSet<IGameplayTag>();
        private readonly Action verifyAccess;

        internal SubjectTagSet(Action verifyAccess) => this.verifyAccess = verifyAccess;

        public event Action<TagSetChange> OnChanged;

        public bool Add(IGameplayTag tag)
        {
            verifyAccess();
            if (tag == null) throw new ArgumentNullException(nameof(tag));
            if (!tags.Add(tag)) return false;
            OnChanged?.Invoke(new TagSetChange(tag, TagSetChangeKind.Added));
            return true;
        }

        public bool Remove(IGameplayTag tag)
        {
            verifyAccess();
            if (!tags.Remove(tag)) return false;
            OnChanged?.Invoke(new TagSetChange(tag, TagSetChangeKind.Removed));
            return true;
        }

        public IEnumerable<IGameplayTag> Values
        {
            get
            {
                verifyAccess();
                return new List<IGameplayTag>(tags);
            }
        }

        internal void ClearListeners() => OnChanged = null;
    }
}
