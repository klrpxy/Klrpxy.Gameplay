using System;
using Klrpxy.Gameplay.Stats;
using Klrpxy.Gameplay.Tags.Runtime;
using Xunit;

namespace KlrpxyGameplayStats.Runtime.Tests
{
    public sealed class StatSubjectTests
    {
        [Fact]
        public void SubjectBindsProvidedStatSet()
        {
            // 验证创建 StatSubject 时会把传入的 StatSet 绑定到该 Subject。
            var statSet = new TestStatSet();

            var subject = new TestSubject(statSet);

            Assert.Same(subject, statSet.Subject);
        }

        [Fact]
        public void StatSetCannotBeBoundToSecondSubject()
        {
            // 验证已绑定的 StatSet 不能再次交给另一个 StatSubject。
            var statSet = new TestStatSet();
            _ = new TestSubject(statSet);

            Assert.Throws<InvalidOperationException>(() => new TestSubject(statSet));
        }

        [Fact]
        public void SubjectRejectsMissingStatSet()
        {
            // 验证创建 StatSubject 时必须提供非空 StatSet。
            Assert.Throws<ArgumentNullException>(() => new TestSubject(null));
        }

        [Fact]
        public void SubjectRejectsNullGeneratedMemberWithItsPath()
        {
            // 验证绑定 StatSet 时会拒绝空成员，并在错误中指出声明路径。
            var statSet = new NullMemberStatSet();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new NullMemberSubject(statSet));

            Assert.Contains("Game::Combat.NullMemberStatSet.Health", exception.Message);
            Assert.Null(statSet.Subject);
        }

        [Fact]
        public void SubjectRejectsMemberAlreadyBoundToAnotherStatSet()
        {
            // 验证一个成员实例不能被两个不同的 StatSet 绑定。
            var health = new Stat(100f);
            _ = new SharedMemberSubject(new SharedMemberStatSet(health));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new SharedMemberSubject(new SharedMemberStatSet(health)));

            Assert.Contains("Game::Combat.SharedMemberStatSet.Health", exception.Message);
        }

        [Fact]
        public void SubjectRejectsDuplicateGeneratedMemberWithoutBindingStatSet()
        {
            // 验证同一成员被两个属性引用时，绑定失败且 StatSet 保持未归属。
            var statSet = new DuplicateMemberStatSet(new Stat(100f));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new DuplicateMemberSubject(statSet));

            Assert.Contains("Game::Combat.DuplicateMemberStatSet.Attack", exception.Message);
            Assert.Null(statSet.Subject);
        }

        [Fact]
        public void SubjectDisposeRemovesDirectModifiersWithoutPublishingChanges()
        {
            // 验证 Subject 重复结束时静默移除直接 Modifier，并拒绝后续新增操作。
            var subject = new LifecycleSubject(new LifecycleStatSet());
            var source = new ModifierSource();
            source.Modify(subject.StatSet.Health).Add(25f);
            var eventCount = 0;
            subject.StatSet.Health.OnFinalValueChanged += (previous, current) => eventCount++;

            subject.Dispose();
            subject.Dispose();
            source.RemoveAllModifiers();

            Assert.Equal(0, eventCount);
            Assert.Throws<ObjectDisposedException>(() =>
                source.Modify(subject.StatSet.Health).Add(10f));
        }

        [Fact]
        public void SubjectDisposeEndsOwnedStatOperations()
        {
            // 验证 Subject 结束后对其 Stat 的后续修改会立即失败。
            var subject = new LifecycleSubject(new LifecycleStatSet());

            subject.Dispose();

            Assert.Throws<ObjectDisposedException>(() => subject.StatSet.Health.BaseValue = 50f);
        }

        [Fact]
        public void SubjectDisposeEndsOwnedRangeAndResourceOperations()
        {
            // 验证 Subject 结束后对其 RangeStat 和 Resource 的后续修改会立即失败。
            var subject = new LifecycleSubject(new LifecycleStatSet());

            subject.Dispose();

            Assert.Throws<ObjectDisposedException>(() => subject.StatSet.Damage.WithBounds(0f, 10f));
            Assert.Throws<ObjectDisposedException>(() => subject.StatSet.Mana.Set(20f));
        }

        [Fact]
        public void GroupRuleAffectsExistingAndLaterMembersUntilTheyLeave()
        {
            // 验证 Group 中的一份共享规则自动影响现有与后加入成员，离开后自动撤销。
            var first = new LifecycleSubject(new LifecycleStatSet());
            var second = new LifecycleSubject(new LifecycleStatSet());
            var group = new StatSubjectGroup();
            var source = new ModifierSource();
            group.Add(first);

            source.For(group).Modify(LifecycleStatSet.HealthKey).Add(25f);
            group.Add(second);

            Assert.Equal(125f, first.StatSet.Health.FinalValue);
            Assert.Equal(125f, second.StatSet.Health.FinalValue);

            group.Remove(first);

            Assert.Equal(100f, first.StatSet.Health.FinalValue);
            Assert.Equal(125f, second.StatSet.Health.FinalValue);
        }

        [Fact]
        public void GroupAppliesRangeRuleOnlyToCompatibleHeterogeneousMembers()
        {
            // 验证异构 Group 只对包含目标 RangeStat Key 的成员应用共享规则。
            var scalarSubject = new LifecycleSubject(new LifecycleStatSet());
            var rangeSubject = new RangeTestSubject(new RangeTestStatSet());
            var group = new StatSubjectGroup();
            var source = new ModifierSource();
            group.Add(scalarSubject);
            group.Add(rangeSubject);

            source.For(group).Modify(RangeTestStatSet.DamageKey).Add(5f);

            Assert.Equal(100f, scalarSubject.StatSet.Health.FinalValue);
            Assert.Equal(
                (15f, 20f),
                (rangeSubject.StatSet.Damage.FinalRange.Min, rangeSubject.StatSet.Damage.FinalRange.Max));
        }

        [Fact]
        public void LocalAndMultipleGroupRulesShareOneCalculationPipeline()
        {
            // 验证本地与多个 Group 的 Modifier 在同一计算阶段聚合。
            var subject = new LifecycleSubject(new LifecycleStatSet());
            var localSource = new ModifierSource();
            var firstGroupSource = new ModifierSource();
            var secondGroupSource = new ModifierSource();
            var firstGroup = new StatSubjectGroup();
            var secondGroup = new StatSubjectGroup();
            firstGroup.Add(subject);
            secondGroup.Add(subject);

            localSource.Modify(subject.StatSet.Health).Add(10f);
            firstGroupSource.For(firstGroup).Modify(LifecycleStatSet.HealthKey).Add(20f);
            secondGroupSource.For(secondGroup).Modify(LifecycleStatSet.HealthKey).AddPercent(50f);

            Assert.Equal(195f, subject.StatSet.Health.FinalValue);
        }

        [Fact]
        public void GroupRejectsDuplicateMemberWithoutChangingRules()
        {
            // 验证 Group 拒绝重复成员，且已有共享规则不会重复应用。
            var subject = new LifecycleSubject(new LifecycleStatSet());
            var group = new StatSubjectGroup();
            var source = new ModifierSource();
            group.Add(subject);
            source.For(group).Modify(LifecycleStatSet.HealthKey).Add(25f);

            Assert.Throws<InvalidOperationException>(() => group.Add(subject));

            Assert.Equal(125f, subject.StatSet.Health.FinalValue);
        }

        [Fact]
        public void GroupRejectsALaterDependencyCycleBeforePublishingAnyMemberChange()
        {
            // 验证批量挂载中后续成员形成依赖环时，前序成员不会暴露瞬态数值或事件。
            var first = new LifecycleSubject(new LifecycleStatSet());
            var second = new LifecycleSubject(new LifecycleStatSet());
            var group = new StatSubjectGroup();
            var source = new ModifierSource();
            var firstEvents = 0;
            first.StatSet.Health.OnFinalValueChanged += (previous, current) => firstEvents++;
            group.Add(first);
            group.Add(second);
            Assert.Throws<InvalidOperationException>(() =>
                source.For(group).Modify(LifecycleStatSet.HealthKey)
                    .Add(second.StatSet.Health, current => current));

            Assert.Equal(100f, first.StatSet.Health.FinalValue);
            Assert.Equal(100f, second.StatSet.Health.FinalValue);
            Assert.Equal(0, firstEvents);
            source.RemoveAllModifiers();
        }

        [Fact]
        public void GroupRejectsASubjectBeforeApplyingEarlierRulesWhenALaterRuleCycles()
        {
            // 验证加入成员时会先验证全部 Group 规则，后续规则失败不会短暂应用前序规则。
            var subject = new LifecycleSubject(new LifecycleStatSet());
            var group = new StatSubjectGroup();
            var source = new ModifierSource();
            var eventCount = 0;
            subject.StatSet.Health.OnFinalValueChanged += (previous, current) => eventCount++;
            source.For(group).Modify(LifecycleStatSet.HealthKey).Add(25f);
            source.For(group).Modify(LifecycleStatSet.HealthKey)
                .Add(subject.StatSet.Health, current => current);

            Assert.Throws<InvalidOperationException>(() => group.Add(subject));

            Assert.Equal(100f, subject.StatSet.Health.FinalValue);
            Assert.Equal(0, eventCount);
            Assert.False(group.Remove(subject));
        }

        [Fact]
        public void GroupHandleSourceAndGroupDisposeRemoveSharedRulesSafely()
        {
            // 验证 Handle、Source 和 Group 都能幂等撤销一份共享规则。
            var subject = new LifecycleSubject(new LifecycleStatSet());
            var group = new StatSubjectGroup();
            var firstSource = new ModifierSource();
            var secondSource = new ModifierSource();
            group.Add(subject);
            ModifierHandle handle = firstSource.For(group).Modify(LifecycleStatSet.HealthKey).Add(10f);
            secondSource.For(group).Modify(LifecycleStatSet.HealthKey).Add(20f);

            handle.Dispose();
            handle.Dispose();
            secondSource.Dispose();
            secondSource.Dispose();
            group.Dispose();
            group.Dispose();

            Assert.Equal(100f, subject.StatSet.Health.FinalValue);
            Assert.Throws<ObjectDisposedException>(() => group.Add(subject));
        }

        [Fact]
        public void SubjectDisposeLeavesAllGroupsAndKeepsOtherMembersActive()
        {
            // 验证 Subject 结束时自动退出全部 Group，而共享规则继续影响其他成员。
            var disposedSubject = new LifecycleSubject(new LifecycleStatSet());
            var survivor = new LifecycleSubject(new LifecycleStatSet());
            var firstGroup = new StatSubjectGroup();
            var secondGroup = new StatSubjectGroup();
            var source = new ModifierSource();
            firstGroup.Add(disposedSubject);
            firstGroup.Add(survivor);
            secondGroup.Add(disposedSubject);
            source.For(firstGroup).Modify(LifecycleStatSet.HealthKey).Add(25f);

            disposedSubject.Dispose();

            Assert.Equal(125f, survivor.StatSet.Health.FinalValue);
            source.Dispose();
            Assert.Equal(100f, survivor.StatSet.Health.FinalValue);
        }

        [Fact]
        public void SubjectDisposeCancelsDynamicValueAndBoundsSubscriptions()
        {
            // 验证 Subject 结束时取消动态 Modifier 与动态边界订阅。
            var subject = new LifecycleSubject(new LifecycleStatSet());
            var source = new ModifierSource();
            var modifierInput = new Stat(10f);
            var minimum = new ObservableValue(0f);
            var maximum = new ObservableValue(200f);
            source.Modify(subject.StatSet.Health).Add(modifierInput, value => value);
            subject.StatSet.Health.WithBounds(ValueInput.External(minimum), ValueInput.External(maximum));

            subject.Dispose();
            modifierInput.BaseValue = 20f;
            maximum.Value = 150f;

            Assert.Throws<ObjectDisposedException>(() => subject.StatSet.Health.BaseValue = 80f);
        }

        [Fact]
        public void DisposedObjectsRejectGroupOperationsBeforeObservableChanges()
        {
            // 验证已结束 Subject 或 Source 的非法 Group 操作在改变成员数值前失败。
            var disposedSubject = new LifecycleSubject(new LifecycleStatSet());
            var liveSubject = new LifecycleSubject(new LifecycleStatSet());
            var disposedSource = new ModifierSource();
            var group = new StatSubjectGroup();
            disposedSubject.Dispose();
            disposedSource.Dispose();
            group.Add(liveSubject);

            Assert.Throws<ObjectDisposedException>(() => group.Add(disposedSubject));
            Assert.Throws<ObjectDisposedException>(() =>
                disposedSource.For(group).Modify(LifecycleStatSet.HealthKey).Add(25f));
            Assert.Equal(100f, liveSubject.StatSet.Health.FinalValue);
        }

        [Fact]
        public void GroupAndSubjectTagsRejectWrongGameplayThread()
        {
            // 验证 Group 和 Subject Tags 修改都受创建时 Gameplay 线程约束。
            var subject = new LifecycleSubject(new LifecycleStatSet());
            var group = new StatSubjectGroup();
            Exception groupError = null;
            Exception tagError = null;
            var thread = new System.Threading.Thread(() =>
            {
                try { group.Add(subject); }
                catch (Exception exception) { groupError = exception; }
                try { subject.Tags.Add(new FakeTag()); }
                catch (Exception exception) { tagError = exception; }
            });

            thread.Start();
            thread.Join();

            Assert.IsType<InvalidOperationException>(groupError);
            Assert.IsType<InvalidOperationException>(tagError);
        }

        private sealed class TestStatSet : StatSet
        {
        }

        private sealed class TestSubject : StatSubject<TestStatSet>
        {
            public TestSubject(TestStatSet statSet)
                : base(statSet)
            {
            }
        }

        private sealed class NullMemberStatSet : StatSet
        {
            public Stat Health { get; }

            protected override void AppendGeneratedMembers(System.Collections.Generic.ICollection<StatMemberDescriptor> members)
            {
                members.Add(CreateMember<Stat>("Game::Combat.NullMemberStatSet.Health", StatMemberKind.Stat, set => ((NullMemberStatSet)set).Health));
            }
        }

        private sealed class NullMemberSubject : StatSubject<NullMemberStatSet>
        {
            public NullMemberSubject(NullMemberStatSet statSet)
                : base(statSet)
            {
            }
        }

        private sealed class SharedMemberStatSet : StatSet
        {
            public SharedMemberStatSet(Stat health) => Health = health;

            public Stat Health { get; }

            protected override void AppendGeneratedMembers(System.Collections.Generic.ICollection<StatMemberDescriptor> members)
            {
                members.Add(CreateMember<Stat>("Game::Combat.SharedMemberStatSet.Health", StatMemberKind.Stat, set => ((SharedMemberStatSet)set).Health));
            }
        }

        private sealed class SharedMemberSubject : StatSubject<SharedMemberStatSet>
        {
            public SharedMemberSubject(SharedMemberStatSet statSet)
                : base(statSet)
            {
            }
        }

        private sealed class DuplicateMemberStatSet : StatSet
        {
            public DuplicateMemberStatSet(Stat value) { Health = value; Attack = value; }
            public Stat Health { get; }
            public Stat Attack { get; }
            protected override void AppendGeneratedMembers(System.Collections.Generic.ICollection<StatMemberDescriptor> members)
            {
                members.Add(CreateMember<Stat>("Game::Combat.DuplicateMemberStatSet.Health", StatMemberKind.Stat, set => ((DuplicateMemberStatSet)set).Health));
                members.Add(CreateMember<Stat>("Game::Combat.DuplicateMemberStatSet.Attack", StatMemberKind.Stat, set => ((DuplicateMemberStatSet)set).Attack));
            }
        }

        private sealed class DuplicateMemberSubject : StatSubject<DuplicateMemberStatSet>
        {
            public DuplicateMemberSubject(DuplicateMemberStatSet statSet) : base(statSet) { }
        }

        private sealed class LifecycleStatSet : StatSet
        {
            public static readonly StatKey<Stat> HealthKey = CreateKey<Stat>(
                typeof(LifecycleStatSet),
                "Game::Combat.LifecycleStatSet.Health",
                set => ((LifecycleStatSet)set).Health);

            public Stat Health { get; } = new Stat(100f);
            public RangeStat Damage { get; } = new RangeStat(10f, 20f);
            public Resource Mana { get; } = new Resource(50f);

            protected override void AppendGeneratedMembers(System.Collections.Generic.ICollection<StatMemberDescriptor> members)
            {
                members.Add(CreateMember<Stat>(
                    "Game::Combat.LifecycleStatSet.Health",
                    StatMemberKind.Stat,
                    set => ((LifecycleStatSet)set).Health));
                members.Add(CreateMember<RangeStat>(
                    "Game::Combat.LifecycleStatSet.Damage",
                    StatMemberKind.RangeStat,
                    set => ((LifecycleStatSet)set).Damage));
                members.Add(CreateMember<Resource>(
                    "Game::Combat.LifecycleStatSet.Mana",
                    StatMemberKind.Resource,
                    set => ((LifecycleStatSet)set).Mana));
            }
        }

        private sealed class LifecycleSubject : StatSubject<LifecycleStatSet>
        {
            public LifecycleSubject(LifecycleStatSet statSet) : base(statSet) { }
        }

        private sealed class FakeTag : IGameplayTag
        {
        }
    }
}
