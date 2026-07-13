using System;
using System.Collections.Generic;

namespace Klrpxy.Gameplay.Stats
{
    public sealed class ModifierSource : IDisposable
    {
        private readonly HashSet<ModifierHandle> handles = new HashSet<ModifierHandle>();
        private bool disposed;
        private readonly GameplayThreadGuard threadGuard = new GameplayThreadGuard();

        internal void ThrowIfDisposed()
        {
            threadGuard.Verify();
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

        internal void VerifyThread() => threadGuard.Verify();

        public void RemoveAllModifiers()
        {
            threadGuard.Verify();
            StatsPropagationCoordinator.Execute(() =>
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
            });
        }

        public void Dispose()
        {
            threadGuard.Verify();
            if (disposed)
            {
                return;
            }

            disposed = true;
            RemoveAllModifiers();
        }
    }
}
