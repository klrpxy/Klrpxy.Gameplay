using System;
using System.Collections.Generic;

namespace Klrpxy.Gameplay.Stats
{
    public sealed class StatSubjectGroup : IDisposable
    {
        private readonly HashSet<StatSubject> members = new HashSet<StatSubject>();
        private readonly List<GroupRule> rules = new List<GroupRule>();
        private readonly GameplayThreadGuard threadGuard = new GameplayThreadGuard();
        private bool disposed;

        public void Add(StatSubject subject)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            if (members.Contains(subject)) throw new InvalidOperationException("The StatSubject already belongs to this group.");

            var prepared = new List<PreparedRuleTarget>();
            try
            {
                foreach (GroupRule rule in rules)
                {
                    prepared.Add(new PreparedRuleTarget(rule, rule.Prepare(subject)));
                }

                subject.JoinGroup(this);
                members.Add(subject);
                StatsPropagationCoordinator.Execute(() =>
                {
                    foreach (PreparedRuleTarget item in prepared) item.Rule.Commit(subject, item.Target);
                    Refresh(prepared);
                });
            }
            catch
            {
                foreach (PreparedRuleTarget item in prepared) item.Rule.Discard(subject, item.Target);
                subject.LeaveGroup(this);
                members.Remove(subject);
                throw;
            }
        }

        public bool Remove(StatSubject subject)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            return Remove(subject, true);
        }

        public ModifierHandle AddModifier(Modifier modifier, ModifierSource source)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            if (source == null) throw new ArgumentNullException(nameof(source));
            source.ThrowIfDisposed();

            var rule = new GroupRule(modifier, StatSubject.NextModifierOrder());
            var prepared = new List<PreparedSubjectTarget>();
            ModifierHandle handle = null;
            try
            {
                foreach (StatSubject member in members)
                {
                    prepared.Add(new PreparedSubjectTarget(member, rule.Prepare(member)));
                }

                rule.Subscribe();
                handle = new ModifierHandle(source, ignored =>
                {
                    rules.Remove(rule);
                    rule.RemoveAllWithoutRefresh();
                }, rule.RefreshAndClearRemoved);
                rule.Handle = handle;
                source.Add(handle);
                rules.Add(rule);
                StatsPropagationCoordinator.Execute(() =>
                {
                    foreach (PreparedSubjectTarget item in prepared) rule.Commit(item.Subject, item.Target);
                    Refresh(prepared);
                });
                return handle;
            }
            catch
            {
                if (handle != null) handle.RemoveWithoutRefresh();
                else
                {
                    rule.DisposePrepared(prepared);
                    rule.DisposeSubscription();
                }
                throw;
            }
        }

        public void Dispose()
        {
            threadGuard.Verify();
            if (disposed) return;
            disposed = true;
            StatsPropagationCoordinator.Execute(() =>
            {
                foreach (GroupRule rule in new List<GroupRule>(rules)) rule.Handle.Dispose();
                foreach (StatSubject member in new List<StatSubject>(members)) Remove(member, true);
            });
        }

        internal void RemoveDisposedSubject(StatSubject subject) => Remove(subject, false);

        internal void TagsChanged(StatSubject subject)
        {
            foreach (GroupRule rule in rules) rule.Update(subject);
        }

        internal void AppendModifiers(StatSubject subject, object target, List<IModifierEntry> result)
        {
            foreach (GroupRule rule in rules)
            {
                if (rule.AppliesTo(subject, target)) result.Add(rule);
            }
        }

        private bool Remove(StatSubject subject, bool refresh)
        {
            if (!members.Remove(subject)) return false;
            StatsPropagationCoordinator.Execute(() =>
            {
                foreach (GroupRule rule in rules) rule.Detach(subject, refresh);
            });
            subject.LeaveGroup(this);
            return true;
        }

        private static void Refresh(List<PreparedRuleTarget> prepared)
        {
            var targets = new HashSet<object>();
            foreach (PreparedRuleTarget item in prepared)
            {
                if (item.Target != null) targets.Add(item.Target.Target);
            }
            StatsPropagationCoordinator.Invalidate(targets);
        }

        private static void Refresh(List<PreparedSubjectTarget> prepared)
        {
            var targets = new HashSet<object>();
            foreach (PreparedSubjectTarget item in prepared)
            {
                if (item.Target != null) targets.Add(item.Target.Target);
            }
            StatsPropagationCoordinator.Invalidate(targets);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(StatSubjectGroup));
        }

        private sealed class GroupRule : IModifierEntry
        {
            private readonly Dictionary<StatSubject, RuleTarget> targets = new Dictionary<StatSubject, RuleTarget>();
            private readonly HashSet<object> removedTargets = new HashSet<object>();
            private IDisposable subscription;

            internal GroupRule(Modifier modifier, long order)
            {
                Modifier = modifier;
                Order = order;
            }

            public Modifier Modifier { get; }

            public long Order { get; }

            internal ModifierHandle Handle { get; set; }

            internal RuleTarget Prepare(StatSubject subject)
            {
                if (Modifier.TargetCondition != null && !Modifier.TargetCondition.Matches(subject.Tags)) return null;
                if (!subject.TryGetModifierTarget(Modifier, out object target)) return null;
                IDisposable dependency = Modifier.DynamicValue == null
                    ? null
                    : StatsPropagationCoordinator.AddDependencies(Modifier.DynamicValue.DependencyNodes, target);
                return new RuleTarget(target, dependency);
            }

            internal void Subscribe()
            {
                if (Modifier.DynamicValue != null) subscription = Modifier.DynamicValue.Subscribe(RefreshAll);
            }

            internal void Commit(StatSubject subject, RuleTarget target)
            {
                if (target != null) targets.Add(subject, target);
            }

            internal void Discard(StatSubject subject, RuleTarget prepared)
            {
                if (targets.TryGetValue(subject, out RuleTarget committed))
                {
                    targets.Remove(subject);
                    committed.Dispose();
                    return;
                }

                prepared?.Dispose();
            }

            internal bool AppliesTo(StatSubject subject, object target)
            {
                return targets.TryGetValue(subject, out RuleTarget state) && ReferenceEquals(state.Target, target);
            }

            internal void Update(StatSubject subject)
            {
                bool matches = Modifier.TargetCondition == null || Modifier.TargetCondition.Matches(subject.Tags);
                if (matches && !targets.ContainsKey(subject))
                {
                    RuleTarget target = Prepare(subject);
                    Commit(subject, target);
                    if (target != null)
                    {
                        try
                        {
                            StatsPropagationCoordinator.Invalidate(target.Target);
                        }
                        catch
                        {
                            Discard(subject, target);
                            throw;
                        }
                    }
                }
                else if (!matches)
                {
                    Detach(subject, true);
                }
            }

            internal void Detach(StatSubject subject, bool refresh)
            {
                if (!targets.TryGetValue(subject, out RuleTarget target)) return;
                targets.Remove(subject);
                target.Dispose();
                if (refresh) StatsPropagationCoordinator.Invalidate(target.Target);
            }

            internal void RemoveAllWithoutRefresh()
            {
                DisposeSubscription();
                foreach (RuleTarget target in targets.Values)
                {
                    target.Dispose();
                    removedTargets.Add(target.Target);
                }
                targets.Clear();
            }

            internal void RefreshAndClearRemoved()
            {
                StatsPropagationCoordinator.Invalidate(removedTargets);
                removedTargets.Clear();
            }

            internal void RefreshAll()
            {
                var nodes = new List<object>();
                foreach (RuleTarget target in targets.Values) nodes.Add(target.Target);
                StatsPropagationCoordinator.Invalidate(nodes);
            }

            internal void DisposePrepared(List<PreparedSubjectTarget> prepared)
            {
                foreach (PreparedSubjectTarget item in prepared) item.Target?.Dispose();
            }

            internal void DisposeSubscription()
            {
                subscription?.Dispose();
                subscription = null;
            }
        }

        private sealed class RuleTarget : IDisposable
        {
            private IDisposable dependency;

            internal RuleTarget(object target, IDisposable dependency)
            {
                Target = target;
                this.dependency = dependency;
            }

            internal object Target { get; }

            public void Dispose()
            {
                dependency?.Dispose();
                dependency = null;
            }
        }

        private readonly struct PreparedRuleTarget
        {
            internal PreparedRuleTarget(GroupRule rule, RuleTarget target) { Rule = rule; Target = target; }
            internal GroupRule Rule { get; }
            internal RuleTarget Target { get; }
        }

        private readonly struct PreparedSubjectTarget
        {
            internal PreparedSubjectTarget(StatSubject subject, RuleTarget target) { Subject = subject; Target = target; }
            internal StatSubject Subject { get; }
            internal RuleTarget Target { get; }
        }
    }
}
