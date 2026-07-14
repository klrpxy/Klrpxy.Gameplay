using System;

namespace Klrpxy.Gameplay.Stats
{
    internal sealed class ModifierAttachment
    {
        private readonly Action remove;
        private readonly Action refresh;
        private bool removed;

        internal ModifierAttachment(Action remove, Action refresh)
        {
            this.remove = remove;
            this.refresh = refresh;
        }

        internal void RemoveWithoutRefresh()
        {
            if (removed) return;
            removed = true;
            remove();
        }

        internal void Refresh() => refresh();
    }
}
