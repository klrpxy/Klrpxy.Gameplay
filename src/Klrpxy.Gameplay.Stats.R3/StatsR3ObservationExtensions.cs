using System;

namespace Klrpxy.Gameplay.Stats.R3
{
    public static class StatsR3ObservationExtensions
    {
        public static global::R3.Observable<float> ObserveFinalValue(this Stat stat)
        {
            if (stat == null) throw new ArgumentNullException(nameof(stat));
            return Observe(
                stat,
                source => source.StatSet?.Subject,
                source => source.FinalValue,
                (source, changed) => source.OnFinalValueChanged += changed,
                (source, changed) => source.OnFinalValueChanged -= changed);
        }

        public static global::R3.Observable<FloatRange> ObserveFinalRange(this RangeStat stat)
        {
            if (stat == null) throw new ArgumentNullException(nameof(stat));
            return Observe(
                stat,
                source => source.StatSet?.Subject,
                source => source.FinalRange,
                (source, changed) => source.OnFinalRangeChanged += changed,
                (source, changed) => source.OnFinalRangeChanged -= changed);
        }

        public static global::R3.Observable<float> ObserveValue(this Resource resource)
        {
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            return Observe(
                resource,
                source => source.StatSet?.Subject,
                source => source.Value,
                (source, changed) => source.OnValueChanged += changed,
                (source, changed) => source.OnValueChanged -= changed);
        }

        private static global::R3.Observable<TValue> Observe<TSource, TValue>(
            TSource source,
            Func<TSource, StatSubject> readSubject,
            Func<TSource, TValue> readCurrent,
            Action<TSource, Action<TValue, TValue>> addChanged,
            Action<TSource, Action<TValue, TValue>> removeChanged)
            where TSource : class
        {
            var sourceReference = new WeakReference<TSource>(source);
            return global::R3.Observable.Create<TValue>(observer =>
            {
                if (!sourceReference.TryGetTarget(out TSource currentSource))
                {
                    observer.OnCompleted(global::R3.Result.Success);
                    return new Subscription(null);
                }

                StatSubject subject = readSubject(currentSource);
                var subjectReference = subject == null ? null : new WeakReference<StatSubject>(subject);
                Action<TValue, TValue> changed = (previous, current) => observer.OnNext(current);
                Action disposed = () => observer.OnCompleted(global::R3.Result.Success);
                addChanged(currentSource, changed);
                if (subject != null) subject.Disposed += disposed;
                try
                {
                    observer.OnNext(readCurrent(currentSource));
                    return new Subscription(() =>
                    {
                        if (sourceReference.TryGetTarget(out TSource liveSource))
                        {
                            removeChanged(liveSource, changed);
                        }
                        if (subjectReference != null
                            && subjectReference.TryGetTarget(out StatSubject liveSubject))
                        {
                            liveSubject.Disposed -= disposed;
                        }
                    });
                }
                catch
                {
                    removeChanged(currentSource, changed);
                    if (subject != null) subject.Disposed -= disposed;
                    throw;
                }
            });
        }

        private sealed class Subscription : IDisposable
        {
            private Action dispose;

            internal Subscription(Action dispose)
            {
                this.dispose = dispose;
            }

            public void Dispose()
            {
                Action current = dispose;
                dispose = null;
                current?.Invoke();
            }
        }
    }
}
