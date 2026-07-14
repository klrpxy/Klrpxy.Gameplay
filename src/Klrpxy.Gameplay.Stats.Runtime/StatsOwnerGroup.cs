using System;
using System.Collections.Generic;

namespace Klrpxy.Gameplay.Stats
{
    public sealed class StatsOwnerGroup : IDisposable
    {
        private readonly HashSet<StatsOwner> members = new HashSet<StatsOwner>();
        private readonly List<GroupRule> rules = new List<GroupRule>();
        private readonly GameplayThreadGuard threadGuard = new GameplayThreadGuard();
        private bool disposed;

        public void Add(StatsOwner owner)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (members.Contains(owner)) throw new InvalidOperationException("The StatsOwner already belongs to this group.");

            var prepared = new List<PreparedRuleTarget>();
            try
            {
                foreach (GroupRule rule in rules)
                {
                    prepared.Add(new PreparedRuleTarget(rule, rule.Prepare(owner)));
                }

                owner.JoinGroup(this);
                members.Add(owner);
                StatsPropagationCoordinator.Execute(() =>
                {
                    foreach (PreparedRuleTarget item in prepared) item.Rule.Commit(owner, item.Target);
                    Refresh(prepared);
                });
            }
            catch
            {
                foreach (PreparedRuleTarget item in prepared) item.Rule.Discard(owner, item.Target);
                owner.LeaveGroup(this);
                members.Remove(owner);
                throw;
            }
        }

        public bool Remove(StatsOwner owner)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            return Remove(owner, true);
        }

        public ModifierHandle AddModifier(Modifier modifier, ModifierSource source)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            if (source == null) throw new ArgumentNullException(nameof(source));
            source.ThrowIfDisposed();

            var rule = new GroupRule(modifier, StatsOwner.NextModifierOrder());
            var prepared = new List<PreparedOwnerTarget>();
            ModifierHandle handle = null;
            try
            {
                foreach (StatsOwner member in members)
                {
                    prepared.Add(new PreparedOwnerTarget(member, rule.Prepare(member)));
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
                    foreach (PreparedOwnerTarget item in prepared) rule.Commit(item.Owner, item.Target);
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
                foreach (StatsOwner member in new List<StatsOwner>(members)) Remove(member, true);
            });
        }

        internal void RemoveDisposedOwner(StatsOwner owner) => Remove(owner, false);

        internal void TagsChanged(StatsOwner owner)
        {
            foreach (GroupRule rule in rules) rule.Update(owner);
        }

        internal void AppendModifiers(StatsOwner owner, object target, List<IModifierEntry> result)
        {
            foreach (GroupRule rule in rules)
            {
                if (rule.AppliesTo(owner, target)) result.Add(rule);
            }
        }

        private bool Remove(StatsOwner owner, bool refresh)
        {
            if (!members.Remove(owner)) return false;
            StatsPropagationCoordinator.Execute(() =>
            {
                foreach (GroupRule rule in rules) rule.Detach(owner, refresh);
            });
            owner.LeaveGroup(this);
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

        private static void Refresh(List<PreparedOwnerTarget> prepared)
        {
            var targets = new HashSet<object>();
            foreach (PreparedOwnerTarget item in prepared)
            {
                if (item.Target != null) targets.Add(item.Target.Target);
            }
            StatsPropagationCoordinator.Invalidate(targets);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(StatsOwnerGroup));
        }

        private sealed class GroupRule : IModifierEntry
        {
            private readonly Dictionary<StatsOwner, RuleTarget> targets = new Dictionary<StatsOwner, RuleTarget>();
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

            internal RuleTarget Prepare(StatsOwner owner)
            {
                if (Modifier.TargetCondition != null && !Modifier.TargetCondition.Matches(owner.Tags)) return null;
                if (!owner.TryGetModifierTarget(Modifier, out object target)) return null;
                IDisposable dependency = Modifier.DynamicValue == null
                    ? null
                    : StatsPropagationCoordinator.AddDependencies(Modifier.DynamicValue.DependencyNodes, target);
                return new RuleTarget(target, dependency);
            }

            internal void Subscribe()
            {
                if (Modifier.DynamicValue != null) subscription = Modifier.DynamicValue.Subscribe(RefreshAll);
            }

            internal void Commit(StatsOwner owner, RuleTarget target)
            {
                if (target != null) targets.Add(owner, target);
            }

            internal void Discard(StatsOwner owner, RuleTarget prepared)
            {
                if (targets.TryGetValue(owner, out RuleTarget committed))
                {
                    targets.Remove(owner);
                    committed.Dispose();
                    return;
                }

                prepared?.Dispose();
            }

            internal bool AppliesTo(StatsOwner owner, object target)
            {
                return targets.TryGetValue(owner, out RuleTarget state) && ReferenceEquals(state.Target, target);
            }

            internal void Update(StatsOwner owner)
            {
                bool matches = Modifier.TargetCondition == null || Modifier.TargetCondition.Matches(owner.Tags);
                if (matches && !targets.ContainsKey(owner))
                {
                    RuleTarget target = Prepare(owner);
                    Commit(owner, target);
                    if (target != null)
                    {
                        try
                        {
                            StatsPropagationCoordinator.Invalidate(target.Target);
                        }
                        catch
                        {
                            Discard(owner, target);
                            throw;
                        }
                    }
                }
                else if (!matches)
                {
                    Detach(owner, true);
                }
            }

            internal void Detach(StatsOwner owner, bool refresh)
            {
                if (!targets.TryGetValue(owner, out RuleTarget target)) return;
                targets.Remove(owner);
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

            internal void DisposePrepared(List<PreparedOwnerTarget> prepared)
            {
                foreach (PreparedOwnerTarget item in prepared) item.Target?.Dispose();
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

        private readonly struct PreparedOwnerTarget
        {
            internal PreparedOwnerTarget(StatsOwner owner, RuleTarget target) { Owner = owner; Target = target; }
            internal StatsOwner Owner { get; }
            internal RuleTarget Target { get; }
        }
    }
}
