using System;

namespace Klrpxy.Gameplay.Stats
{
    public static class ValueInput
    {
        public static ValueInput<float> Final(Stat stat)
        {
            if (stat == null) throw new ArgumentNullException(nameof(stat));
            return new ValueInput<float>(() => stat.FinalValue, callback =>
            {
                stat.FinalValueChanged += callback;
                return new Subscription(() => stat.FinalValueChanged -= callback);
            }, stat, stat.VerifyThread);
        }

        public static ValueInput<FloatRange> Final(RangeStat stat)
        {
            if (stat == null) throw new ArgumentNullException(nameof(stat));
            return new ValueInput<FloatRange>(() => stat.FinalRange, callback =>
            {
                stat.FinalRangeChanged += callback;
                return new Subscription(() => stat.FinalRangeChanged -= callback);
            }, stat, stat.VerifyThread);
        }

        public static ValueInput<float> Current(Resource resource)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            return new ValueInput<float>(() => resource.Value, callback =>
            {
                resource.ValueChanged += callback;
                return new Subscription(() => resource.ValueChanged -= callback);
            }, resource, resource.VerifyThread);
        }

        public static ValueInput<float> Base(Stat stat)
        {
            if (stat == null) throw new ArgumentNullException(nameof(stat));
            return new ValueInput<float>(() => stat.BaseValue, callback =>
            {
                Action<float, float> listener = (previous, current) => callback();
                stat.OnBaseValueChanged += listener;
                return new Subscription(() => stat.OnBaseValueChanged -= listener);
            }, null, stat.VerifyThread);
        }

        public static ValueInput<float> External(ObservableValue value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return new ValueInput<float>(() => value.Value, callback =>
            {
                value.Changed += callback;
                return new Subscription(() => value.Changed -= callback);
            }, null, value.VerifyThread);
        }

        private sealed class Subscription : IDisposable
        {
            private Action unsubscribe;

            internal Subscription(Action unsubscribe) => this.unsubscribe = unsubscribe;

            public void Dispose()
            {
                Action action = unsubscribe;
                unsubscribe = null;
                action?.Invoke();
            }
        }
    }

    public sealed class ValueInput<T> : IValueInput
    {
        private readonly Func<T> read;
        private readonly Func<Action, IDisposable> subscribe;
        private readonly Action verifyThread;
        private readonly object dependencyNode;

        internal ValueInput(
            Func<T> read,
            Func<Action, IDisposable> subscribe,
            object dependencyNode,
            Action verifyThread)
        {
            this.read = read;
            this.subscribe = subscribe;
            this.dependencyNode = dependencyNode;
            this.verifyThread = verifyThread;
        }

        internal T Read()
        {
            verifyThread();
            return read();
        }

        internal IDisposable Subscribe(Action callback)
        {
            verifyThread();
            return subscribe(callback);
        }

        object IValueInput.Read() => Read();

        IDisposable IValueInput.Subscribe(Action callback) => Subscribe(callback);

        object IValueInput.DependencyNode => dependencyNode;

        internal object DependencyNode => dependencyNode;
    }

    internal interface IValueInput
    {
        object Read();

        IDisposable Subscribe(Action callback);

        object DependencyNode { get; }
    }

    public enum ResourceBoundPolicy
    {
        Clamp,
        PreserveRatio
    }
}
