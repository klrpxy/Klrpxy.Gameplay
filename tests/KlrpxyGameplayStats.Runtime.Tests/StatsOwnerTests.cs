using System;
using Klrpxy.Gameplay.Stats;
using Klrpxy.Gameplay.Tags.Runtime;
using Xunit;

namespace KlrpxyGameplayStats.Runtime.Tests
{
    public sealed class StatsOwnerTests
    {
        [Fact]
        public void OwnerBindsProvidedStatSet()
        {
            // 验证创建 StatsOwner 时会把传入的 StatSet 绑定到该 Owner。
            var statSet = new TestStatSet();

            var owner = new TestOwner(statSet);

            Assert.Same(owner, statSet.Owner);
        }

        [Fact]
        public void StatSetCannotBeBoundToSecondOwner()
        {
            // 验证已绑定的 StatSet 不能再次交给另一个 StatsOwner。
            var statSet = new TestStatSet();
            _ = new TestOwner(statSet);

            Assert.Throws<InvalidOperationException>(() => new TestOwner(statSet));
        }

        [Fact]
        public void OwnerRejectsMissingStatSet()
        {
            // 验证创建 StatsOwner 时必须提供非空 StatSet。
            Assert.Throws<ArgumentNullException>(() => new TestOwner(null));
        }

        [Fact]
        public void OwnerRejectsNullGeneratedMemberWithItsPath()
        {
            // 验证绑定 StatSet 时会拒绝空成员，并在错误中指出声明路径。
            var statSet = new NullMemberStatSet();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new NullMemberOwner(statSet));

            Assert.Contains("Game::Combat.NullMemberStatSet.Health", exception.Message);
            Assert.Null(statSet.Owner);
        }

        [Fact]
        public void OwnerRejectsMemberAlreadyBoundToAnotherStatSet()
        {
            // 验证一个成员实例不能被两个不同的 StatSet 绑定。
            var health = new Stat(100f);
            _ = new SharedMemberOwner(new SharedMemberStatSet(health));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new SharedMemberOwner(new SharedMemberStatSet(health)));

            Assert.Contains("Game::Combat.SharedMemberStatSet.Health", exception.Message);
        }

        [Fact]
        public void OwnerRejectsDuplicateGeneratedMemberWithoutBindingStatSet()
        {
            // 验证同一成员被两个属性引用时，绑定失败且 StatSet 保持未归属。
            var statSet = new DuplicateMemberStatSet(new Stat(100f));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new DuplicateMemberOwner(statSet));

            Assert.Contains("Game::Combat.DuplicateMemberStatSet.Attack", exception.Message);
            Assert.Null(statSet.Owner);
        }

        [Fact]
        public void OwnerDisposeRemovesDirectModifiersWithoutPublishingChanges()
        {
            // 验证 Owner 重复结束时静默移除直接 Modifier，并拒绝后续新增操作。
            var owner = new LifecycleOwner(new LifecycleStatSet());
            var source = new ModifierSource();
            owner.AddModifier(Modifier.Flat(25f, LifecycleStatSet.HealthKey), source);
            var eventCount = 0;
            owner.StatSet.Health.OnFinalValueChanged += (previous, current) => eventCount++;

            owner.Dispose();
            owner.Dispose();
            source.RemoveAllModifiers();

            Assert.Equal(0, eventCount);
            Assert.Throws<ObjectDisposedException>(() =>
                owner.AddModifier(Modifier.Flat(10f, LifecycleStatSet.HealthKey), source));
        }

        [Fact]
        public void ConditionalOwnerModifierRejectsAnIncompatibleKeyBeforeRegistration()
        {
            // 验证条件当前不匹配时，Owner 仍会原子拒绝不属于自身 StatSet 的 Key。
            var owner = new LifecycleOwner(new LifecycleStatSet());
            var source = new ModifierSource();
            Modifier modifier = Modifier.Flat(10f, RangeTestStatSet.DamageKey)
                .WhenTargetMatches(new NeverMatchesQuery());

            Assert.Throws<InvalidOperationException>(() => owner.AddModifier(modifier, source));

            source.RemoveAllModifiers();
            Assert.Equal(100f, owner.StatSet.Health.FinalValue);
        }

        [Fact]
        public void OwnerDisposeEndsOwnedStatOperations()
        {
            // 验证 Owner 结束后对其 Stat 的后续修改会立即失败。
            var owner = new LifecycleOwner(new LifecycleStatSet());

            owner.Dispose();

            Assert.Throws<ObjectDisposedException>(() => owner.StatSet.Health.BaseValue = 50f);
        }

        [Fact]
        public void OwnerDisposeEndsOwnedRangeAndResourceOperations()
        {
            // 验证 Owner 结束后对其 RangeStat 和 Resource 的后续修改会立即失败。
            var owner = new LifecycleOwner(new LifecycleStatSet());

            owner.Dispose();

            Assert.Throws<ObjectDisposedException>(() => owner.StatSet.Damage.WithBounds(0f, 10f));
            Assert.Throws<ObjectDisposedException>(() => owner.StatSet.Mana.Set(20f));
        }

        [Fact]
        public void GroupRuleAffectsExistingAndLaterMembersUntilTheyLeave()
        {
            // 验证 Group 中的一份共享规则自动影响现有与后加入成员，离开后自动撤销。
            var first = new LifecycleOwner(new LifecycleStatSet());
            var second = new LifecycleOwner(new LifecycleStatSet());
            var group = new StatsOwnerGroup();
            var source = new ModifierSource();
            group.Add(first);

            group.AddModifier(Modifier.Flat(25f, LifecycleStatSet.HealthKey), source);
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
            var scalarOwner = new LifecycleOwner(new LifecycleStatSet());
            var rangeOwner = new RangeTestOwner(new RangeTestStatSet());
            var group = new StatsOwnerGroup();
            var source = new ModifierSource();
            group.Add(scalarOwner);
            group.Add(rangeOwner);

            group.AddModifier(Modifier.Flat(5f, RangeTestStatSet.DamageKey), source);

            Assert.Equal(100f, scalarOwner.StatSet.Health.FinalValue);
            Assert.Equal(
                (15f, 20f),
                (rangeOwner.StatSet.Damage.FinalRange.Min, rangeOwner.StatSet.Damage.FinalRange.Max));
        }

        [Fact]
        public void LocalAndMultipleGroupRulesShareOneCalculationPipeline()
        {
            // 验证本地与多个 Group 的 Modifier 在同一计算阶段聚合。
            var owner = new LifecycleOwner(new LifecycleStatSet());
            var localSource = new ModifierSource();
            var firstGroupSource = new ModifierSource();
            var secondGroupSource = new ModifierSource();
            var firstGroup = new StatsOwnerGroup();
            var secondGroup = new StatsOwnerGroup();
            firstGroup.Add(owner);
            secondGroup.Add(owner);

            owner.AddModifier(Modifier.Flat(10f, LifecycleStatSet.HealthKey), localSource);
            firstGroup.AddModifier(Modifier.Flat(20f, LifecycleStatSet.HealthKey), firstGroupSource);
            secondGroup.AddModifier(Modifier.Percent(50f, LifecycleStatSet.HealthKey), secondGroupSource);

            Assert.Equal(195f, owner.StatSet.Health.FinalValue);
        }

        [Fact]
        public void GroupRejectsDuplicateMemberWithoutChangingRules()
        {
            // 验证 Group 拒绝重复成员，且已有共享规则不会重复应用。
            var owner = new LifecycleOwner(new LifecycleStatSet());
            var group = new StatsOwnerGroup();
            var source = new ModifierSource();
            group.Add(owner);
            group.AddModifier(Modifier.Flat(25f, LifecycleStatSet.HealthKey), source);

            Assert.Throws<InvalidOperationException>(() => group.Add(owner));

            Assert.Equal(125f, owner.StatSet.Health.FinalValue);
        }

        [Fact]
        public void GroupRejectsALaterDependencyCycleBeforePublishingAnyMemberChange()
        {
            // 验证批量挂载中后续成员形成依赖环时，前序成员不会暴露瞬态数值或事件。
            var first = new LifecycleOwner(new LifecycleStatSet());
            var second = new LifecycleOwner(new LifecycleStatSet());
            var group = new StatsOwnerGroup();
            var source = new ModifierSource();
            var firstEvents = 0;
            first.StatSet.Health.OnFinalValueChanged += (previous, current) => firstEvents++;
            group.Add(first);
            group.Add(second);
            ModifierValue value = ModifierValue.From(
                ValueInput.Final(second.StatSet.Health),
                current => current);

            Assert.Throws<InvalidOperationException>(() =>
                group.AddModifier(Modifier.Flat(value, LifecycleStatSet.HealthKey), source));

            Assert.Equal(100f, first.StatSet.Health.FinalValue);
            Assert.Equal(100f, second.StatSet.Health.FinalValue);
            Assert.Equal(0, firstEvents);
            source.RemoveAllModifiers();
        }

        [Fact]
        public void GroupRejectsAnOwnerBeforeApplyingEarlierRulesWhenALaterRuleCycles()
        {
            // 验证加入成员时会先验证全部 Group 规则，后续规则失败不会短暂应用前序规则。
            var owner = new LifecycleOwner(new LifecycleStatSet());
            var group = new StatsOwnerGroup();
            var source = new ModifierSource();
            var eventCount = 0;
            owner.StatSet.Health.OnFinalValueChanged += (previous, current) => eventCount++;
            group.AddModifier(Modifier.Flat(25f, LifecycleStatSet.HealthKey), source);
            group.AddModifier(
                Modifier.Flat(
                    ModifierValue.From(ValueInput.Final(owner.StatSet.Health), current => current),
                    LifecycleStatSet.HealthKey),
                source);

            Assert.Throws<InvalidOperationException>(() => group.Add(owner));

            Assert.Equal(100f, owner.StatSet.Health.FinalValue);
            Assert.Equal(0, eventCount);
            Assert.False(group.Remove(owner));
        }

        [Fact]
        public void GroupHandleSourceAndGroupDisposeRemoveSharedRulesSafely()
        {
            // 验证 Handle、Source 和 Group 都能幂等撤销一份共享规则。
            var owner = new LifecycleOwner(new LifecycleStatSet());
            var group = new StatsOwnerGroup();
            var firstSource = new ModifierSource();
            var secondSource = new ModifierSource();
            group.Add(owner);
            ModifierHandle handle = group.AddModifier(Modifier.Flat(10f, LifecycleStatSet.HealthKey), firstSource);
            group.AddModifier(Modifier.Flat(20f, LifecycleStatSet.HealthKey), secondSource);

            handle.Dispose();
            handle.Dispose();
            secondSource.Dispose();
            secondSource.Dispose();
            group.Dispose();
            group.Dispose();

            Assert.Equal(100f, owner.StatSet.Health.FinalValue);
            Assert.Throws<ObjectDisposedException>(() => group.Add(owner));
        }

        [Fact]
        public void OwnerDisposeLeavesAllGroupsAndKeepsOtherMembersActive()
        {
            // 验证 Owner 结束时自动退出全部 Group，而共享规则继续影响其他成员。
            var disposedOwner = new LifecycleOwner(new LifecycleStatSet());
            var survivor = new LifecycleOwner(new LifecycleStatSet());
            var firstGroup = new StatsOwnerGroup();
            var secondGroup = new StatsOwnerGroup();
            var source = new ModifierSource();
            firstGroup.Add(disposedOwner);
            firstGroup.Add(survivor);
            secondGroup.Add(disposedOwner);
            firstGroup.AddModifier(Modifier.Flat(25f, LifecycleStatSet.HealthKey), source);

            disposedOwner.Dispose();

            Assert.Equal(125f, survivor.StatSet.Health.FinalValue);
            source.Dispose();
            Assert.Equal(100f, survivor.StatSet.Health.FinalValue);
        }

        [Fact]
        public void OwnerDisposeCancelsDynamicValueAndBoundsSubscriptions()
        {
            // 验证 Owner 结束时取消动态 Modifier 与动态边界订阅。
            var owner = new LifecycleOwner(new LifecycleStatSet());
            var source = new ModifierSource();
            var modifierInput = new ObservableValue(10f);
            var minimum = new ObservableValue(0f);
            var maximum = new ObservableValue(200f);
            owner.AddModifier(
                Modifier.Flat(
                    ModifierValue.From(ValueInput.External(modifierInput), value => value),
                    LifecycleStatSet.HealthKey),
                source);
            owner.StatSet.Health.WithBounds(ValueInput.External(minimum), ValueInput.External(maximum));

            owner.Dispose();
            modifierInput.Value = 20f;
            maximum.Value = 150f;

            Assert.Throws<ObjectDisposedException>(() => owner.StatSet.Health.BaseValue = 80f);
        }

        [Fact]
        public void DisposedObjectsRejectGroupOperationsBeforeObservableChanges()
        {
            // 验证已结束 Owner 或 Source 的非法 Group 操作在改变成员数值前失败。
            var disposedOwner = new LifecycleOwner(new LifecycleStatSet());
            var liveOwner = new LifecycleOwner(new LifecycleStatSet());
            var disposedSource = new ModifierSource();
            var group = new StatsOwnerGroup();
            disposedOwner.Dispose();
            disposedSource.Dispose();
            group.Add(liveOwner);

            Assert.Throws<ObjectDisposedException>(() => group.Add(disposedOwner));
            Assert.Throws<ObjectDisposedException>(() =>
                group.AddModifier(Modifier.Flat(25f, LifecycleStatSet.HealthKey), disposedSource));
            Assert.Equal(100f, liveOwner.StatSet.Health.FinalValue);
        }

        [Fact]
        public async System.Threading.Tasks.Task GroupAndOwnerTagsRejectWrongGameplayThread()
        {
            // 验证 Group 和 Owner Tags 修改都受创建时 Gameplay 线程约束。
            var owner = new LifecycleOwner(new LifecycleStatSet());
            var group = new StatsOwnerGroup();
            Exception groupError = await System.Threading.Tasks.Task.Run(() =>
            {
                try { group.Add(owner); return null; }
                catch (Exception exception) { return exception; }
            });
            Exception tagError = await System.Threading.Tasks.Task.Run(() =>
            {
                try { owner.Tags.Add(new FakeTag()); return null; }
                catch (Exception exception) { return exception; }
            });

            Assert.IsType<InvalidOperationException>(groupError);
            Assert.IsType<InvalidOperationException>(tagError);
        }

        private sealed class TestStatSet : StatSet
        {
        }

        private sealed class TestOwner : StatsOwner<TestStatSet>
        {
            public TestOwner(TestStatSet statSet)
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

        private sealed class NullMemberOwner : StatsOwner<NullMemberStatSet>
        {
            public NullMemberOwner(NullMemberStatSet statSet)
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

        private sealed class SharedMemberOwner : StatsOwner<SharedMemberStatSet>
        {
            public SharedMemberOwner(SharedMemberStatSet statSet)
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

        private sealed class DuplicateMemberOwner : StatsOwner<DuplicateMemberStatSet>
        {
            public DuplicateMemberOwner(DuplicateMemberStatSet statSet) : base(statSet) { }
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

        private sealed class LifecycleOwner : StatsOwner<LifecycleStatSet>
        {
            public LifecycleOwner(LifecycleStatSet statSet) : base(statSet) { }
        }

        private sealed class NeverMatchesQuery : ITagQuery
        {
            public bool Matches(ITagSet tags) => false;
        }

        private sealed class FakeTag : IGameplayTag
        {
        }
    }
}
