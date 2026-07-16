using System;
using System.Collections.Generic;

namespace Klrpxy.Gameplay.Stats
{
    internal sealed class ModifierCondition
    {
        private readonly Func<bool> read;
        private readonly Func<Action, IDisposable> subscribe;

        internal ModifierCondition(Func<bool> read, Func<Action, IDisposable> subscribe)
        {
            this.read = read ?? throw new ArgumentNullException(nameof(read));
            this.subscribe = subscribe ?? throw new ArgumentNullException(nameof(subscribe));
        }

        internal bool Read() => read();

        internal IDisposable Subscribe(Action callback) => subscribe(callback);

        internal static ModifierCondition Combine(ModifierCondition first, ModifierCondition second)
        {
            if (first == null) return second;
            if (second == null) return first;
            return new ModifierCondition(
                () => first.Read() && second.Read(),
                callback => SubscribeBoth(first, second, callback));
        }

        private static IDisposable SubscribeBoth(
            ModifierCondition first,
            ModifierCondition second,
            Action callback)
        {
            IDisposable firstSubscription = first.Subscribe(callback);
            try
            {
                return new CombinedSubscription(firstSubscription, second.Subscribe(callback));
            }
            catch
            {
                firstSubscription?.Dispose();
                throw;
            }
        }

        private sealed class CombinedSubscription : IDisposable
        {
            private List<IDisposable> subscriptions;

            internal CombinedSubscription(params IDisposable[] subscriptions)
            {
                this.subscriptions = new List<IDisposable>(subscriptions);
            }

            public void Dispose()
            {
                if (subscriptions == null) return;
                foreach (IDisposable subscription in subscriptions) subscription?.Dispose();
                subscriptions = null;
            }
        }
    }
}
