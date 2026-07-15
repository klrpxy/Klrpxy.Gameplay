using System;
using System.Collections.Generic;

namespace Klrpxy.Gameplay.Stats
{
    internal static class StatsPropagationCoordinator
    {
        private const int EventBudget = 1024;
        [ThreadStatic]
        private static ThreadState threadState;

        private static ThreadState State => threadState ?? (threadState = new ThreadState());
        private static Queue<Action> notifications => State.Notifications;
        private static Dictionary<object, Dictionary<object, int>> dependencies => State.Dependencies;
        private static Dictionary<object, PendingChange> changes => State.Changes;
        private static List<object> changeOrder => State.ChangeOrder;
        private static int mutationDepth { get => State.MutationDepth; set => State.MutationDepth = value; }
        private static bool dispatching { get => State.Dispatching; set => State.Dispatching = value; }

        internal static void Execute(Action mutation)
        {
            bool outermost = mutationDepth == 0;
            mutationDepth++;
            try
            {
                mutation();
            }
            finally
            {
                mutationDepth--;
                if (outermost)
                {
                    CompleteRound();
                    DrainNotifications();
                }
            }
        }

        internal static void DiscardCurrentRound()
        {
            changes.Clear();
            changeOrder.Clear();
        }

        internal static void RecordChange<T>(object node, Func<Action<T, T>> getListeners, T previous, T current)
        {
            if (mutationDepth == 0)
            {
                Execute(() => RecordChange(node, getListeners, previous, current));
                return;
            }

            if (changes.TryGetValue(node, out PendingChange existing))
            {
                existing.Current = current;
                return;
            }

            changes.Add(node, new PendingChange
            {
                Previous = previous,
                Current = current,
                CreateDispatch = (oldValue, newValue) =>
                    () => DispatchListeners(getListeners(), (T)oldValue, (T)newValue)
            });
            changeOrder.Add(node);
        }

        internal static void Invalidate(IEnumerable<object> nodes)
        {
            Execute(() =>
            {
                foreach (object node in nodes)
                {
                    if (node is Stat stat) stat.RecalculateForCoordinator();
                    else ((RangeStat)node).RecalculateForCoordinator();
                }
            });
        }

        internal static void Invalidate(object node)
        {
            Invalidate(new[] { node });
        }

        private static void CompleteRound()
        {
            foreach (object node in changeOrder)
            {
                PendingChange change = changes[node];
                if (!Equals(change.Previous, change.Current))
                {
                    notifications.Enqueue(change.CreateDispatch(change.Previous, change.Current));
                }
            }

            changes.Clear();
            changeOrder.Clear();
        }

        private static void DrainNotifications()
        {
            if (dispatching) return;
            dispatching = true;
            int dispatched = 0;
            try
            {
                while (notifications.Count > 0)
                {
                    if (++dispatched > EventBudget)
                    {
                        notifications.Clear();
                        StatsDiagnostics.Report(new InvalidOperationException("The Stats event feedback budget was exceeded."));
                        break;
                    }

                    try
                    {
                        notifications.Dequeue()();
                    }
                    catch (Exception exception)
                    {
                        StatsDiagnostics.Report(exception);
                    }
                }
            }
            finally
            {
                dispatching = false;
            }
        }

        private static void DispatchListeners<T>(Action<T, T> listeners, T previous, T current)
        {
            if (listeners == null) return;
            foreach (Delegate item in listeners.GetInvocationList())
            {
                try
                {
                    ((Action<T, T>)item)(previous, current);
                }
                catch (Exception exception)
                {
                    StatsDiagnostics.Report(exception);
                }
            }
        }

        internal static IDisposable AddDependencies(IEnumerable<object> sources, object target)
        {
            var addedSources = new List<object>();
            foreach (object source in sources)
            {
                if (ReferenceEquals(source, target) || CanReach(target, source, new HashSet<object>()))
                {
                    throw new InvalidOperationException("The Stats dependency would create a cycle.");
                }

                addedSources.Add(source);
            }

            foreach (object source in addedSources)
            {
                if (!dependencies.TryGetValue(source, out Dictionary<object, int> targets))
                {
                    targets = new Dictionary<object, int>();
                    dependencies.Add(source, targets);
                }

                targets.TryGetValue(target, out int count);
                targets[target] = count + 1;
            }

            return new DependencyRegistration(addedSources, target);
        }

        internal static void RemoveNode(object node)
        {
            dependencies.Remove(node);
            var emptySources = new List<object>();
            foreach (KeyValuePair<object, Dictionary<object, int>> dependency in dependencies)
            {
                dependency.Value.Remove(node);
                if (dependency.Value.Count == 0) emptySources.Add(dependency.Key);
            }

            foreach (object source in emptySources) dependencies.Remove(source);
        }

        private static bool CanReach(object source, object target, HashSet<object> visited)
        {
            if (ReferenceEquals(source, target)) return true;
            if (!visited.Add(source) || !dependencies.TryGetValue(source, out Dictionary<object, int> targets)) return false;
            foreach (object next in targets.Keys)
            {
                if (CanReach(next, target, visited)) return true;
            }

            return false;
        }

        private sealed class PendingChange
        {
            internal object Previous;
            internal object Current;
            internal Func<object, object, Action> CreateDispatch;
        }

        private sealed class ThreadState
        {
            internal readonly Queue<Action> Notifications = new Queue<Action>();
            internal readonly Dictionary<object, Dictionary<object, int>> Dependencies = new Dictionary<object, Dictionary<object, int>>();
            internal readonly Dictionary<object, PendingChange> Changes = new Dictionary<object, PendingChange>();
            internal readonly List<object> ChangeOrder = new List<object>();
            internal int MutationDepth;
            internal bool Dispatching;
        }

        private sealed class DependencyRegistration : IDisposable
        {
            private List<object> sources;
            private readonly object target;

            internal DependencyRegistration(List<object> sources, object target)
            {
                this.sources = sources;
                this.target = target;
            }

            public void Dispose()
            {
                if (sources == null) return;
                foreach (object source in sources)
                {
                    if (!dependencies.TryGetValue(source, out Dictionary<object, int> targets) ||
                        !targets.TryGetValue(target, out int currentCount))
                    {
                        continue;
                    }

                    int count = currentCount - 1;
                    if (count == 0) targets.Remove(target); else targets[target] = count;
                    if (targets.Count == 0) dependencies.Remove(source);
                }

                sources = null;
            }
        }
    }
}
