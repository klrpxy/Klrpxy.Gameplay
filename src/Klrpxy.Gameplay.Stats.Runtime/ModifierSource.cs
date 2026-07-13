using System;
using System.Collections.Generic;

namespace Klrpxy.Gameplay.Stats
{
    public sealed class ModifierSource : IDisposable
    {
        private readonly HashSet<ModifierHandle> handles = new HashSet<ModifierHandle>();
        private bool disposed;

        internal void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ModifierSource));
            }
        }

        internal void Add(ModifierHandle handle)
        {
            ThrowIfDisposed();
            handles.Add(handle);
        }

        internal void Remove(ModifierHandle handle)
        {
            handles.Remove(handle);
        }

        public void RemoveAllModifiers()
        {
            var refreshes = new HashSet<Action>();
            foreach (ModifierHandle handle in new List<ModifierHandle>(handles))
            {
                handle.RemoveForSource(refreshes);
            }

            foreach (Action refresh in refreshes)
            {
                refresh();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            RemoveAllModifiers();
        }
    }
}
