using System;
using System.Collections.Generic;

namespace Klrpxy.Gameplay.Stats
{
    public sealed class ModifierHandle : IDisposable
    {
        private readonly ModifierSource source;
        private readonly Action<ModifierHandle> remove;
        private readonly Action refresh;
        private bool disposed;

        internal ModifierHandle(ModifierSource source, Action<ModifierHandle> remove, Action refresh)
        {
            this.source = source;
            this.remove = remove;
            this.refresh = refresh;
        }

        public void Dispose()
        {
            source.VerifyThread();
            if (Remove()) refresh();
        }

        internal void RemoveForSource(ISet<Action> refreshes)
        {
            if (Remove()) refreshes.Add(refresh);
        }

        private bool Remove()
        {
            if (disposed) return false;

            disposed = true;
            remove(this);
            source.Remove(this);
            return true;
        }
    }
}
