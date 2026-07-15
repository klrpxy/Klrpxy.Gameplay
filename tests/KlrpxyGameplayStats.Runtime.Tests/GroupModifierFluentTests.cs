using System;
using System.Collections.Generic;
using System.Linq;
using Klrpxy.Gameplay.Stats;
using Klrpxy.Gameplay.Tags.Runtime;
using Xunit;

namespace KlrpxyGameplayStats.Runtime.Tests
{
    public sealed class GroupModifierFluentTests
    {
        [Fact]
        public void GroupFluentRuleAppliesToExistingCompatibleMembers()
        {
            var subject = new TestSubject(new TestStatSet());
            var group = new StatSubjectGroup();
            var source = new ModifierSource();
            group.Add(subject);

            ModifierHandle handle = source.For(group).Modify(TestStatSet.PowerKey).Add(5f);

            Assert.Equal(15f, subject.StatSet.Power.FinalValue);
            handle.Dispose();
            Assert.Equal(10f, subject.StatSet.Power.FinalValue);
        }

        [Fact]
        public void GroupCanChainSingleAndSequenceAddsForExistingAndFutureMembers()
        {
            var first = new TestSubject(new TestStatSet());
            var second = new TestSubject(new TestStatSet());
            var third = new TestSubject(new TestStatSet());
            var group = new StatSubjectGroup()
                .Add(first)
                .Add(new[] { second });
            var source = new ModifierSource();

            source.For(group).Modify(TestStatSet.PowerKey).Add(5f);
            group.Add(third);

            Assert.Equal(15f, first.StatSet.Power.FinalValue);
            Assert.Equal(15f, second.StatSet.Power.FinalValue);
            Assert.Equal(15f, third.StatSet.Power.FinalValue);
            group.Remove(second);
            Assert.Equal(10f, second.StatSet.Power.FinalValue);
        }

        [Fact]
        public void BatchAddPreparationFailureLeavesNoMemberValueEventOrDependency()
        {
            var subject = new TestSubject(new TestStatSet());
            var group = new StatSubjectGroup();
            var source = new ModifierSource();
            var eventCount = 0;
            subject.StatSet.Armor.OnFinalValueChanged += (previous, current) => eventCount++;
            group.AddModifier(
                Modifier.Flat(
                    ModifierValue.From(ValueInput.Final(subject.StatSet.Power), value => value),
                    TestStatSet.ArmorKey),
                source);
            group.AddModifier(
                Modifier.Flat(
                    ModifierValue.From(ValueInput.Final(subject.StatSet.Power), value => value),
                    TestStatSet.PowerKey),
                source);

            Assert.Throws<InvalidOperationException>(() => group.Add(new[] { subject }));

            Assert.Equal(10f, subject.StatSet.Power.FinalValue);
            Assert.Equal(20f, subject.StatSet.Armor.FinalValue);
            Assert.Equal(0, eventCount);
            Assert.False(group.Remove(subject));

            var retrySource = new ModifierSource();
            ModifierHandle retry = subject.AddModifier(
                Modifier.Flat(
                    ModifierValue.From(ValueInput.Final(subject.StatSet.Armor), value => value),
                    TestStatSet.PowerKey),
                retrySource);
            Assert.Equal(30f, subject.StatSet.Power.FinalValue);
            retry.Dispose();
        }

        [Fact]
        public void GroupBuilderCanBeReusedForFiveIndependentFixedOperations()
        {
            var subject = new TestSubject(new TestStatSet());
            var group = new StatSubjectGroup().Add(subject);
            var source = new ModifierSource();
            GroupStatModifierBuilder power = source.For(group).Modify(TestStatSet.PowerKey);

            power.Add(5f);
            power.AddPercent(50f);
            power.Multiply(2f);
            power.Override(100f, priority: 10);
            ModifierHandle clamp = power.Clamp(0f, 80f);

            Assert.Equal(80f, subject.StatSet.Power.FinalValue);
            clamp.Dispose();
            Assert.Equal(100f, subject.StatSet.Power.FinalValue);
            source.Dispose();
            Assert.Equal(10f, subject.StatSet.Power.FinalValue);
        }

        [Fact]
        public void GeneratedRangeKeyExposesTheFiveFixedGroupOperations()
        {
            var subject = new TestSubject(new TestStatSet());
            var group = new StatSubjectGroup().Add(subject);
            var source = new ModifierSource();
            GroupRangeStatModifierBuilder damage = source.For(group).Modify(TestStatSet.DamageKey);

            damage.Add(1f);
            damage.AddPercent(100f);
            damage.Multiply(2f);
            damage.Override(new FloatRange(30f, 40f), priority: 10);
            damage.Clamp(32f, 38f);

            Assert.Equal((32f, 38f), (subject.StatSet.Damage.FinalRange.Min, subject.StatSet.Damage.FinalRange.Max));
            source.Dispose();
            Assert.Equal((2f, 4f), (subject.StatSet.Damage.FinalRange.Min, subject.StatSet.Damage.FinalRange.Max));
        }

        [Fact]
        public void TagConditionsAreEvaluatedPerTargetAndAlwaysCombinedWithAnd()
        {
            var item = new TestTag("Item");
            var quick = new TestTag("Item.Quick", item);
            var fire = new TestTag("Item.Fire", item);
            var quickItem = new TestSubject(new TestStatSet(), quick);
            var fireItem = new TestSubject(new TestStatSet(), quick, fire);
            var ordinaryItem = new TestSubject(new TestStatSet(), item);
            var group = new StatSubjectGroup().Add(new[] { quickItem, fireItem, ordinaryItem });
            var source = new ModifierSource();
            GroupModifierScopeBuilder quickNonFire = source.For(group)
                .WhereTargetHas(item)
                .WhereTargetHas(quick)
                .WhereTargetMatches(new DoesNotHaveQuery(fire));

            quickNonFire.Modify(TestStatSet.PowerKey).Add(5f);

            Assert.Equal(15f, quickItem.StatSet.Power.FinalValue);
            Assert.Equal(10f, fireItem.StatSet.Power.FinalValue);
            Assert.Equal(10f, ordinaryItem.StatSet.Power.FinalValue);

            quickItem.AddTag(fire);
            Assert.Equal(10f, quickItem.StatSet.Power.FinalValue);
            quickItem.RemoveTag(fire);
            Assert.Equal(15f, quickItem.StatSet.Power.FinalValue);
        }

        [Fact]
        public void InvalidBatchInputsLeaveEveryCandidateOutsideTheGroup()
        {
            var group = new StatSubjectGroup();
            var existing = new TestSubject(new TestStatSet());
            group.Add(existing);

            var nullCandidate = new TestSubject(new TestStatSet());
            Assert.Throws<ArgumentNullException>(() => group.Add((IEnumerable<StatSubject>)null));
            Assert.Throws<ArgumentException>(() => group.Add(new StatSubject[] { nullCandidate, null }));
            Assert.False(group.Remove(nullCandidate));

            var duplicate = new TestSubject(new TestStatSet());
            Assert.Throws<InvalidOperationException>(() => group.Add(new StatSubject[] { duplicate, duplicate }));
            Assert.False(group.Remove(duplicate));

            var beforeExisting = new TestSubject(new TestStatSet());
            Assert.Throws<InvalidOperationException>(() => group.Add(new StatSubject[] { beforeExisting, existing }));
            Assert.False(group.Remove(beforeExisting));

            var beforeDisposed = new TestSubject(new TestStatSet());
            var disposed = new TestSubject(new TestStatSet());
            disposed.Dispose();
            Assert.Throws<ObjectDisposedException>(() => group.Add(new StatSubject[] { beforeDisposed, disposed }));
            Assert.False(group.Remove(beforeDisposed));

            var beforeEnumerationFailure = new TestSubject(new TestStatSet());
            Assert.Throws<InvalidOperationException>(() => group.Add(FailingSequence(beforeEnumerationFailure)));
            Assert.False(group.Remove(beforeEnumerationFailure));
        }

        [Fact]
        public void BatchCommitFailureRollsBackFinalValuesEventsAndMembership()
        {
            var first = new TestSubject(new TestStatSet());
            var second = new TestSubject(new TestStatSet());
            var group = new StatSubjectGroup();
            var source = new ModifierSource();
            var input = new ObservableValue(5f);
            var reads = 0;
            var firstEvents = 0;
            first.StatSet.Power.OnFinalValueChanged += (previous, current) => firstEvents++;
            group.AddModifier(
                Modifier.Flat(
                    ModifierValue.From(ValueInput.External(input), value =>
                    {
                        if (++reads == 2) throw new InvalidOperationException("Calculation failed.");
                        return value;
                    }),
                    TestStatSet.PowerKey),
                source);

            Assert.Throws<InvalidOperationException>(() => group.Add(new StatSubject[] { first, second }));

            Assert.Equal(10f, first.StatSet.Power.FinalValue);
            Assert.Equal(10f, second.StatSet.Power.FinalValue);
            Assert.Equal(0, firstEvents);
            Assert.False(group.Remove(first));
            Assert.False(group.Remove(second));
        }

        [Fact]
        public void GroupTerminalFailureRollsBackEveryMemberValueEventAndRegistration()
        {
            var first = new TestSubject(new TestStatSet(10f));
            var overflowing = new TestSubject(new TestStatSet(float.MaxValue));
            var group = new StatSubjectGroup().Add(new StatSubject[] { first, overflowing });
            var source = new ModifierSource();
            var firstEvents = 0;
            first.StatSet.Power.OnFinalValueChanged += (previous, current) => firstEvents++;

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                source.For(group).Modify(TestStatSet.PowerKey).Multiply(2f));

            Assert.Equal(10f, first.StatSet.Power.FinalValue);
            Assert.Equal(float.MaxValue, overflowing.StatSet.Power.FinalValue);
            Assert.Equal(0, firstEvents);
            ModifierHandle retry = source.For(group).Modify(TestStatSet.PowerKey).Add(1f);
            Assert.Equal(11f, first.StatSet.Power.FinalValue);
            retry.Dispose();
        }

        [Fact]
        public void GroupAndConditionBuildersCanBeReusedForkedAndFailAfterTheirLifetimeEnds()
        {
            var quick = new TestTag("Quick");
            var matching = new TestSubject(new TestStatSet(), quick);
            var other = new TestSubject(new TestStatSet());
            var group = new StatSubjectGroup().Add(new StatSubject[] { matching, other });
            var source = new ModifierSource();
            GroupModifierScopeBuilder all = source.For(group);
            GroupModifierScopeBuilder onlyQuick = all.WhereTargetHas(quick);

            all.Modify(TestStatSet.PowerKey).Add(1f);
            onlyQuick.Modify(TestStatSet.PowerKey).Add(5f);

            Assert.Equal(16f, matching.StatSet.Power.FinalValue);
            Assert.Equal(11f, other.StatSet.Power.FinalValue);

            GroupStatModifierBuilder endedSourceBuilder = source.For(group).Modify(TestStatSet.PowerKey);
            source.Dispose();
            Assert.Throws<ObjectDisposedException>(() => endedSourceBuilder.Add(1f));

            var liveSource = new ModifierSource();
            GroupStatModifierBuilder endedGroupBuilder = liveSource.For(group).Modify(TestStatSet.PowerKey);
            group.Dispose();
            Assert.Throws<ObjectDisposedException>(() => endedGroupBuilder.Add(1f));
        }

        private static IEnumerable<StatSubject> FailingSequence(StatSubject first)
        {
            yield return first;
            throw new InvalidOperationException("Enumeration failed.");
        }

        private sealed class TestStatSet : StatSet
        {
            public TestStatSet(float power = 10f) => Power = new Stat(power);

            public static readonly StatKey<Stat> PowerKey = CreateKey<Stat>(
                typeof(TestStatSet),
                "Tests.TestStatSet.Power",
                set => ((TestStatSet)set).Power);

            public static readonly StatKey<Stat> ArmorKey = CreateKey<Stat>(
                typeof(TestStatSet),
                "Tests.TestStatSet.Armor",
                set => ((TestStatSet)set).Armor);

            public static readonly StatKey<RangeStat> DamageKey = CreateKey<RangeStat>(
                typeof(TestStatSet),
                "Tests.TestStatSet.Damage",
                set => ((TestStatSet)set).Damage);

            public Stat Power { get; }
            public Stat Armor { get; } = new Stat(20f);
            public RangeStat Damage { get; } = new RangeStat(2f, 4f);

            protected override void AppendGeneratedMembers(ICollection<StatMemberDescriptor> members)
            {
                members.Add(CreateMember<Stat>(
                    "Tests.TestStatSet.Power",
                    StatMemberKind.Stat,
                    set => ((TestStatSet)set).Power));
                members.Add(CreateMember<Stat>(
                    "Tests.TestStatSet.Armor",
                    StatMemberKind.Stat,
                    set => ((TestStatSet)set).Armor));
                members.Add(CreateMember<RangeStat>(
                    "Tests.TestStatSet.Damage",
                    StatMemberKind.RangeStat,
                    set => ((TestStatSet)set).Damage));
            }
        }

        private sealed class TestSubject : StatSubject<TestStatSet>
        {
            public TestSubject(TestStatSet statSet, params IGameplayTag[] tags)
                : base(statSet, tags)
            {
            }
        }

        private sealed class TestTag : IHierarchicalGameplayTag
        {
            private readonly TestTag parent;

            public TestTag(string path, TestTag parent = null)
            {
                Path = path;
                this.parent = parent;
            }

            public string Path { get; }

            public bool IsSameOrDescendantOf(IGameplayTag tag)
            {
                for (TestTag current = this; current != null; current = current.parent)
                {
                    if (ReferenceEquals(current, tag)) return true;
                }

                return false;
            }
        }

        private sealed class DoesNotHaveQuery : ITagQuery
        {
            private readonly IGameplayTag tag;

            public DoesNotHaveQuery(IGameplayTag tag) => this.tag = tag;

            public bool Matches(ITagSet tags) => !tags.Values.Any(candidate =>
            {
                var hierarchical = candidate as IHierarchicalGameplayTag;
                return hierarchical != null
                    ? hierarchical.IsSameOrDescendantOf(tag)
                    : ReferenceEquals(candidate, tag);
            });
        }
    }
}
