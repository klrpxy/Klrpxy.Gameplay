using System;
using System.Collections.Generic;
using System.Threading;
using Klrpxy.Gameplay.Tags.Runtime;

namespace Klrpxy.Gameplay.Stats
{
    public abstract class StatSubject : IDisposable
    {
        private static long nextModifierOrder;
        private readonly GameplayThreadGuard threadGuard = new GameplayThreadGuard();
        private readonly HashSet<ModifierHandle> directHandles = new HashSet<ModifierHandle>();
        private readonly HashSet<StatSubjectGroup> groups = new HashSet<StatSubjectGroup>();
        private readonly List<ConditionalRule> conditionalRules = new List<ConditionalRule>();
        private readonly SubjectTagSet tags;
        private bool disposed;

        internal event Action Disposed;

        protected StatSubject(StatSet statSet)
            : this(statSet, Array.Empty<IGameplayTag>())
        {
        }

        protected StatSubject(StatSet statSet, params IGameplayTag[] initialTags)
        {
            if (statSet == null)
            {
                throw new ArgumentNullException(nameof(statSet));
            }

            if (initialTags == null) throw new ArgumentNullException(nameof(initialTags));
            tags = new SubjectTagSet(VerifyTagAccess);
            foreach (IGameplayTag tag in initialTags) tags.Add(tag);
            tags.OnChanged += TagsChanged;
            StatSet = statSet;
            statSet.Bind(this);
        }

        public StatSet StatSet { get; }

        public ITagSet Tags => tags;

        public bool AddTag(IGameplayTag tag) => tags.Add(tag);

        public bool RemoveTag(IGameplayTag tag) => tags.Remove(tag);

        public ModifierHandle AddModifier(Modifier modifier, ModifierSource source)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            if (modifier == null)
            {
                throw new ArgumentNullException(nameof(modifier));
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            source.ThrowIfDisposed();
            if (!HasModifierTarget(modifier))
            {
                throw new InvalidOperationException("The Modifier target is not declared by this StatSubject.");
            }

            long order = NextModifierOrder();
            if (modifier.TargetCondition != null || modifier.Condition != null)
            {
                return AddConditionalModifier(modifier, source, order, null);
            }
            if (modifier.Target is StatKey<Stat> statKey && statKey.TryGet(StatSet, out Stat stat))
            {
                ModifierHandle handle = stat.AddModifier(modifier, source, order);
                directHandles.Add(handle);
                return handle;
            }

            if (modifier.Target is StatKey<RangeStat> rangeKey && rangeKey.TryGet(StatSet, out RangeStat rangeStat))
            {
                ModifierHandle handle = rangeStat.AddModifier(modifier, source, order);
                directHandles.Add(handle);
                return handle;
            }

            throw new InvalidOperationException("The Modifier target is not declared by this StatSubject.");
        }

        internal ModifierHandle AddDirectModifier(Modifier modifier, Stat stat, ModifierSource source)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            source.ThrowIfDisposed();
            if (stat == null || stat.StatSet?.Subject != this)
            {
                throw new InvalidOperationException("The Stat target is not declared by this StatSubject.");
            }

            if (modifier.Condition != null)
            {
                return AddConditionalModifier(modifier, source, NextModifierOrder(), stat);
            }

            ModifierHandle handle = null;
            StatsPropagationCoordinator.Execute(() =>
            {
                try
                {
                    handle = stat.AddModifier(modifier, source, NextModifierOrder());
                }
                catch
                {
                    stat.RecalculateForCoordinator();
                    StatsPropagationCoordinator.DiscardCurrentRound();
                    throw;
                }
            });
            directHandles.Add(handle);
            return handle;
        }

        private bool HasModifierTarget(Modifier modifier)
        {
            if (modifier.Target is StatKey<Stat> statKey) return statKey.TryGet(StatSet, out Stat _);
            return modifier.Target is StatKey<RangeStat> rangeKey && rangeKey.TryGet(StatSet, out RangeStat _);
        }

        internal static long NextModifierOrder() => Interlocked.Increment(ref nextModifierOrder);

        internal bool TryGetModifierTarget(Modifier modifier, out object target)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            if (modifier.Target is StatKey<Stat> statKey && statKey.TryGet(StatSet, out Stat stat))
            {
                target = stat;
                return true;
            }

            if (modifier.Target is StatKey<RangeStat> rangeKey && rangeKey.TryGet(StatSet, out RangeStat rangeStat))
            {
                target = rangeStat;
                return true;
            }

            target = null;
            return false;
        }

        internal void AppendGroupModifiers(object target, List<IModifierEntry> result)
        {
            foreach (StatSubjectGroup group in groups) group.AppendModifiers(this, target, result);
        }

        internal void JoinGroup(StatSubjectGroup group)
        {
            VerifyCanJoinGroup();
            groups.Add(group);
        }

        internal void VerifyCanJoinGroup()
        {
            threadGuard.Verify();
            ThrowIfDisposed();
        }

        internal void LeaveGroup(StatSubjectGroup group) => groups.Remove(group);

        private ModifierHandle AddConditionalModifier(Modifier modifier, ModifierSource source, long order, object target)
        {
            var rule = new ConditionalRule(this, modifier, order, target);
            ModifierHandle handle = null;
            handle = new ModifierHandle(source, ignored =>
            {
                conditionalRules.Remove(rule);
                rule.DisposeWithoutRefresh();
            }, rule.RefreshRemoved);
            try
            {
                rule.Subscribe();
                StatsPropagationCoordinator.Execute(rule.Update);
                source.Add(handle);
                conditionalRules.Add(rule);
                directHandles.Add(handle);
                return handle;
            }
            catch
            {
                rule.DisposeWithoutRefresh();
                rule.RefreshRemoved();
                throw;
            }
        }

        private void TagsChanged(TagSetChange change)
        {
            StatsPropagationCoordinator.Execute(() =>
            {
                foreach (ConditionalRule rule in conditionalRules) rule.Update();
                foreach (StatSubjectGroup group in groups) group.TagsChanged(this);
            });
        }

        private void ConditionChanged(ConditionalRule rule)
        {
            threadGuard.Verify();
            ThrowIfDisposed();
            StatsPropagationCoordinator.Execute(rule.Update);
        }

        public void Dispose()
        {
            threadGuard.Verify();
            if (disposed) return;
            disposed = true;
            foreach (StatSubjectGroup group in new List<StatSubjectGroup>(groups)) group.RemoveDisposedSubject(this);
            groups.Clear();
            foreach (ModifierHandle handle in directHandles) handle.RemoveWithoutRefresh();
            directHandles.Clear();
            conditionalRules.Clear();
            tags.OnChanged -= TagsChanged;
            tags.ClearListeners();
            StatSet.DisposeMembers();
            Disposed?.Invoke();
            Disposed = null;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(StatSubject));
        }

        private void VerifyTagAccess()
        {
            threadGuard.Verify();
            ThrowIfDisposed();
        }

        private sealed class ConditionalRule
        {
            private readonly StatSubject subject;
            private readonly Modifier modifier;
            private readonly long order;
            private readonly object target;
            private ModifierAttachment attachment;
            private ModifierAttachment removedAttachment;
            private IDisposable conditionSubscription;

            internal ConditionalRule(StatSubject subject, Modifier modifier, long order, object target)
            {
                this.subject = subject;
                this.modifier = modifier;
                this.order = order;
                this.target = target;
            }

            internal void Subscribe()
            {
                if (modifier.Condition != null)
                {
                    conditionSubscription = modifier.Condition.Subscribe(
                        () => subject.ConditionChanged(this));
                }
            }

            internal void Update()
            {
                bool matches = modifier.Matches(subject.Tags);
                if (matches && attachment == null)
                {
                    attachment = subject.AttachDirectModifier(modifier, order, target);
                }
                else if (!matches && attachment != null)
                {
                    DetachWithoutRefresh();
                    RefreshRemoved();
                }
            }

            private void DetachWithoutRefresh()
            {
                if (attachment == null) return;
                removedAttachment = attachment;
                attachment = null;
                removedAttachment.RemoveWithoutRefresh();
            }

            internal void DisposeWithoutRefresh()
            {
                conditionSubscription?.Dispose();
                conditionSubscription = null;
                DetachWithoutRefresh();
            }

            internal void RefreshRemoved()
            {
                removedAttachment?.Refresh();
                removedAttachment = null;
            }
        }

        private ModifierAttachment AttachDirectModifier(Modifier modifier, long order, object directTarget)
        {
            if (directTarget is Stat directStat)
            {
                var directRegistration = new Stat.ModifierRegistration(modifier, order);
                directRegistration.Subscribe(directStat.RecalculateForCoordinator, directStat);
                return AttachDirectModifier(
                    directStat,
                    directRegistration,
                    () => StatsPropagationCoordinator.Invalidate(directStat));
            }

            if (modifier.Target is StatKey<Stat> statKey && statKey.TryGet(StatSet, out Stat stat))
            {
                var registration = new Stat.ModifierRegistration(modifier, order);
                registration.Subscribe(stat.RecalculateForCoordinator, stat);
                return AttachDirectModifier(stat, registration, () => StatsPropagationCoordinator.Invalidate(stat));
            }

            var rangeKey = (StatKey<RangeStat>)modifier.Target;
            rangeKey.TryGet(StatSet, out RangeStat rangeStat);
            var rangeRegistration = new Stat.ModifierRegistration(modifier, order);
            rangeRegistration.Subscribe(rangeStat.RecalculateForCoordinator, rangeStat);
            return AttachDirectModifier(rangeStat, rangeRegistration, () => StatsPropagationCoordinator.Invalidate(rangeStat));
        }

        private static ModifierAttachment AttachDirectModifier(
            object target,
            Stat.ModifierRegistration registration,
            Action refresh)
        {
            if (target is Stat stat)
            {
                stat.AddConditionalRegistration(registration);
                return new ModifierAttachment(() => stat.RemoveConditionalRegistration(registration), refresh);
            }

            var range = (RangeStat)target;
            range.AddConditionalRegistration(registration);
            return new ModifierAttachment(() => range.RemoveConditionalRegistration(registration), refresh);
        }
    }

    public abstract class StatSubject<TStatSet> : StatSubject
        where TStatSet : StatSet
    {
        protected StatSubject(TStatSet statSet)
            : base(statSet)
        {
        }


        protected StatSubject(TStatSet statSet, params IGameplayTag[] initialTags)
            : base(statSet, initialTags)
        {
        }

        public new TStatSet StatSet => (TStatSet)base.StatSet;
    }
}
