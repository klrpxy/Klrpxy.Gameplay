using System;
using System.Collections.Generic;

namespace Klrpxy.Gameplay.Stats
{
    public sealed class ModifierValue
    {
        private readonly IValueInput[] inputs;
        private readonly Func<object[], float> calculate;

        private ModifierValue(IValueInput[] inputs, Func<object[], float> calculate)
        {
            this.inputs = inputs;
            this.calculate = calculate;
        }

        public static ModifierValue From<T>(ValueInput<T> input, Func<T, float> calculate)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (calculate == null) throw new ArgumentNullException(nameof(calculate));
            return new ModifierValue(new IValueInput[] { input }, values => calculate((T)values[0]));
        }

        public static ModifierValue From<TFirst, TSecond>(
            ValueInput<TFirst> first,
            ValueInput<TSecond> second,
            Func<TFirst, TSecond, float> calculate)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));
            if (calculate == null) throw new ArgumentNullException(nameof(calculate));
            return new ModifierValue(
                new IValueInput[] { first, second },
                values => calculate((TFirst)values[0], (TSecond)values[1]));
        }

        public static ModifierValue From<TFirst, TSecond, TThird>(
            ValueInput<TFirst> first,
            ValueInput<TSecond> second,
            ValueInput<TThird> third,
            Func<TFirst, TSecond, TThird, float> calculate)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));
            if (third == null) throw new ArgumentNullException(nameof(third));
            if (calculate == null) throw new ArgumentNullException(nameof(calculate));
            return new ModifierValue(
                new IValueInput[] { first, second, third },
                values => calculate((TFirst)values[0], (TSecond)values[1], (TThird)values[2]));
        }

        internal float Read()
        {
            var values = new object[inputs.Length];
            for (int index = 0; index < inputs.Length; index++) values[index] = inputs[index].Read();
            float result = calculate(values);
            Modifier.ValidateFinite(result, nameof(result));
            return result;
        }

        internal IDisposable Subscribe(Action callback)
        {
            var subscriptions = new List<IDisposable>();
            try
            {
                foreach (IValueInput input in inputs) subscriptions.Add(input.Subscribe(callback));
                return new CompositeSubscription(subscriptions);
            }
            catch
            {
                foreach (IDisposable subscription in subscriptions) subscription?.Dispose();
                throw;
            }
        }

        internal IEnumerable<object> DependencyNodes
        {
            get
            {
                foreach (IValueInput input in inputs)
                {
                    if (input.DependencyNode != null) yield return input.DependencyNode;
                }
            }
        }

        private sealed class CompositeSubscription : IDisposable
        {
            private List<IDisposable> subscriptions;

            internal CompositeSubscription(List<IDisposable> subscriptions) => this.subscriptions = subscriptions;

            public void Dispose()
            {
                if (subscriptions == null) return;
                foreach (IDisposable subscription in subscriptions) subscription?.Dispose();
                subscriptions = null;
            }
        }
    }
}
