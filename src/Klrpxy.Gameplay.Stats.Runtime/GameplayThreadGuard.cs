using System;
using System.Threading;

namespace Klrpxy.Gameplay.Stats
{
    internal sealed class GameplayThreadGuard
    {
        private readonly int threadId = Thread.CurrentThread.ManagedThreadId;

        internal void Verify()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId)
            {
                throw new InvalidOperationException("Stats objects must be modified from their Gameplay thread.");
            }
        }
    }
}
