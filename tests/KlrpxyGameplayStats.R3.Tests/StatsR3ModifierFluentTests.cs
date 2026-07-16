using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Klrpxy.Gameplay.Stats.R3;
using Klrpxy.Gameplay.Tags.Runtime;
using R3;
using Xunit;

namespace Klrpxy.Gameplay.Stats.R3.Tests
{
    public sealed class StatsR3ModifierFluentTests
    {
        [Fact]
        public void DirectR3ValueAppliesCurrentAndFutureValues()
        {
            var subject = new TestSubject();
            var source = new ModifierSource();
            var value = new ReactiveProperty<float>(5f);

            source.Modify(subject.StatSet.Power).Add(value);

            Assert.Equal(15f, subject.StatSet.Power.FinalValue);
            value.Value = 8f;
            Assert.Equal(18f, subject.StatSet.Power.FinalValue);
        }

        [Fact]
        public void DirectR3ValueSupportsEveryDynamicOperation()
        {
            var addPercent = new TestSubject();
            var multiply = new TestSubject();
            var overridden = new TestSubject();
            var source = new ModifierSource();
            var value = new ReactiveProperty<float>(50f);

            source.Modify(addPercent.StatSet.Power).AddPercent(value);
            source.Modify(multiply.StatSet.Power).Multiply(value);
            source.Modify(overridden.StatSet.Power).Override(value, priority: 7);

            Assert.Equal(15f, addPercent.StatSet.Power.FinalValue);
            Assert.Equal(500f, multiply.StatSet.Power.FinalValue);
            Assert.Equal(50f, overridden.StatSet.Power.FinalValue);

            value.Value = 2f;
            Assert.Equal(10.2f, addPercent.StatSet.Power.FinalValue, 3);
            Assert.Equal(20f, multiply.StatSet.Power.FinalValue);
            Assert.Equal(2f, overridden.StatSet.Power.FinalValue);
        }

        [Fact]
        public void DirectR3ConditionImmediatelyControlsTheModifierAndTracksChanges()
        {
            var subject = new TestSubject();
            var source = new ModifierSource();
            var condition = new ReactiveProperty<bool>(false);

            source.Modify(subject.StatSet.Power)
                .Where(condition)
                .Add(5f);

            Assert.Equal(10f, subject.StatSet.Power.FinalValue);
            condition.Value = true;
            Assert.Equal(15f, subject.StatSet.Power.FinalValue);
            condition.Value = false;
            Assert.Equal(10f, subject.StatSet.Power.FinalValue);
        }

        [Fact]
        public void GroupRuleSharesOneR3ConditionAndDynamicValueAcrossMembers()
        {
            var first = new TestSubject();
            var second = new TestSubject();
            var group = new StatSubjectGroup().Add(new StatSubject[] { first, second });
            var source = new ModifierSource();
            var condition = new ReactiveProperty<bool>(true);
            var value = new ReactiveProperty<float>(5f);

            source.For(group)
                .Where(condition)
                .Modify(TestStatSet.PowerKey)
                .Add(value);

            Assert.Equal(15f, first.StatSet.Power.FinalValue);
            Assert.Equal(15f, second.StatSet.Power.FinalValue);
            value.Value = 8f;
            Assert.Equal(18f, first.StatSet.Power.FinalValue);
            Assert.Equal(18f, second.StatSet.Power.FinalValue);
            condition.Value = false;
            Assert.Equal(10f, first.StatSet.Power.FinalValue);
            Assert.Equal(10f, second.StatSet.Power.FinalValue);
        }

        [Fact]
        public void GroupRuleSharesStatRangeAndResourceInputsAcrossMembers()
        {
            var first = new TestSubject();
            var second = new TestSubject();
            var input = new TestSubject();
            var group = new StatSubjectGroup().Add(new StatSubject[] { first, second });
            var source = new ModifierSource();

            source.For(group).Modify(TestStatSet.PowerKey)
                .Add(input.StatSet.Power, value => value * 0.5f);
            source.For(group).Modify(TestStatSet.PowerKey)
                .AddPercent(input.StatSet.Attack, range => range.Max);
            source.For(group).Modify(TestStatSet.PowerKey)
                .Multiply(input.StatSet.Health, value => value * 0.1f);

            Assert.Equal(33f, first.StatSet.Power.FinalValue);
            Assert.Equal(33f, second.StatSet.Power.FinalValue);
            input.StatSet.Power.BaseValue = 20f;
            input.AddModifier(Modifier.Flat(10f, TestStatSet.AttackKey), new ModifierSource());
            input.StatSet.Health.Set(30f);
            Assert.Equal(72f, first.StatSet.Power.FinalValue);
            Assert.Equal(72f, second.StatSet.Power.FinalValue);
        }

        [Fact]
        public void GroupR3AndTagConditionsAlwaysCombineWithAndPerTarget()
        {
            var quick = new TestTag("Item.Quick");
            var matching = new TestSubject(quick);
            var other = new TestSubject();
            var group = new StatSubjectGroup().Add(new StatSubject[] { matching, other });
            var source = new ModifierSource();
            var enabled = new ReactiveProperty<bool>(true);

            source.For(group)
                .Where(enabled)
                .WhereTargetHas(quick)
                .Modify(TestStatSet.PowerKey)
                .Add(5f);

            Assert.Equal(15f, matching.StatSet.Power.FinalValue);
            Assert.Equal(10f, other.StatSet.Power.FinalValue);
            enabled.Value = false;
            Assert.Equal(10f, matching.StatSet.Power.FinalValue);
            enabled.Value = true;
            matching.RemoveTag(quick);
            Assert.Equal(10f, matching.StatSet.Power.FinalValue);
        }

        [Fact]
        public void CompletedR3InputsFreezeAndFailuresReportDiagnostics()
        {
            var subject = new TestSubject();
            var source = new ModifierSource();
            var value = new ReactiveProperty<float>(5f);
            var condition = new ReactiveProperty<bool>(true);
            var failure = new InvalidOperationException("R3 input failed.");
            Exception reported = null;
            Action<Exception> previousHandler = StatsDiagnostics.EventExceptionHandler;
            StatsDiagnostics.EventExceptionHandler = exception => reported = exception;
            try
            {
                source.Modify(subject.StatSet.Power)
                    .Where(condition)
                    .Add(value);

                value.OnCompleted(Result.Success);
                condition.OnCompleted(Result.Failure(failure));
                subject.StatSet.Power.BaseValue = 20f;

                Assert.Same(failure, reported);
                Assert.Equal(25f, subject.StatSet.Power.FinalValue);
            }
            finally
            {
                StatsDiagnostics.EventExceptionHandler = previousHandler;
            }
        }

        [Fact]
        public void StatsCleanupNeverDisposesCallerOwnedR3Inputs()
        {
            var handleSubject = new TestSubject();
            var handleSource = new ModifierSource();
            var handleValue = new ReactiveProperty<float>(5f);
            ModifierHandle handle = handleSource.Modify(handleSubject.StatSet.Power).Add(handleValue);
            handle.Dispose();
            handleValue.Value = 8f;
            Assert.False(handleValue.IsDisposed);
            Assert.Equal(10f, handleSubject.StatSet.Power.FinalValue);

            var sourceSubject = new TestSubject();
            var source = new ModifierSource();
            var sourceCondition = new ReactiveProperty<bool>(true);
            source.Modify(sourceSubject.StatSet.Power).Where(sourceCondition).Add(5f);
            source.Dispose();
            sourceCondition.Value = false;
            Assert.False(sourceCondition.IsDisposed);
            Assert.Equal(10f, sourceSubject.StatSet.Power.FinalValue);

            var disposedSubject = new TestSubject();
            var subjectValue = new ReactiveProperty<float>(5f);
            new ModifierSource().Modify(disposedSubject.StatSet.Power).Add(subjectValue);
            disposedSubject.Dispose();
            subjectValue.Value = 9f;
            Assert.False(subjectValue.IsDisposed);

            var groupSubject = new TestSubject();
            var group = new StatSubjectGroup().Add(groupSubject);
            var groupValue = new ReactiveProperty<float>(5f);
            new ModifierSource().For(group).Modify(TestStatSet.PowerKey).Add(groupValue);
            group.Dispose();
            groupValue.Value = 9f;
            Assert.False(groupValue.IsDisposed);
            Assert.Equal(10f, groupSubject.StatSet.Power.FinalValue);
        }

        [Fact]
        public void R3DynamicValueRejectsInvalidInitialStateAndRecoversFromLaterFailures()
        {
            var subject = new TestSubject();
            var source = new ModifierSource();
            var value = new ReactiveProperty<float>(float.NaN);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                source.Modify(subject.StatSet.Power).Add(value));
            Assert.Equal(10f, subject.StatSet.Power.FinalValue);

            value.Value = 5f;
            source.Modify(subject.StatSet.Power).Add(value);
            Exception reported = null;
            Action<Exception> previousHandler = StatsDiagnostics.EventExceptionHandler;
            StatsDiagnostics.EventExceptionHandler = exception => reported = exception;
            try
            {
                value.Value = float.PositiveInfinity;
                Assert.IsType<ArgumentOutOfRangeException>(reported);
                Assert.Equal(15f, subject.StatSet.Power.FinalValue);

                value.Value = 7f;
                Assert.Equal(17f, subject.StatSet.Power.FinalValue);
            }
            finally
            {
                StatsDiagnostics.EventExceptionHandler = previousHandler;
            }
        }

        [Fact]
        public void GroupR3ValueSupportsTheAllowedStatAndRangeOperations()
        {
            var percentStat = new TestSubject();
            var multiplyStat = new TestSubject();
            var overrideStat = new TestSubject();
            var addRange = new TestSubject();
            var percentRange = new TestSubject();
            var multiplyRange = new TestSubject();
            var value = new ReactiveProperty<float>(50f);
            var source = new ModifierSource();

            source.For(new StatSubjectGroup().Add(percentStat))
                .Modify(TestStatSet.PowerKey).AddPercent(value);
            source.For(new StatSubjectGroup().Add(multiplyStat))
                .Modify(TestStatSet.PowerKey).Multiply(value);
            source.For(new StatSubjectGroup().Add(overrideStat))
                .Modify(TestStatSet.PowerKey).Override(value, priority: 4);
            source.For(new StatSubjectGroup().Add(addRange))
                .Modify(TestStatSet.AttackKey).Add(value);
            source.For(new StatSubjectGroup().Add(percentRange))
                .Modify(TestStatSet.AttackKey).AddPercent(value);
            source.For(new StatSubjectGroup().Add(multiplyRange))
                .Modify(TestStatSet.AttackKey).Multiply(value);

            Assert.Equal(15f, percentStat.StatSet.Power.FinalValue);
            Assert.Equal(500f, multiplyStat.StatSet.Power.FinalValue);
            Assert.Equal(50f, overrideStat.StatSet.Power.FinalValue);
            Assert.Equal(new FloatRange(55f, 60f), addRange.StatSet.Attack.FinalRange);
            Assert.Equal(new FloatRange(7.5f, 15f), percentRange.StatSet.Attack.FinalRange);
            Assert.Equal(new FloatRange(250f, 500f), multiplyRange.StatSet.Attack.FinalRange);
        }

        [Fact]
        public void MultipleR3ConditionsAndEitherTagOrderingAlwaysUseAnd()
        {
            var direct = new TestSubject();
            var first = new ReactiveProperty<bool>(true);
            var second = new ReactiveProperty<bool>(false);
            new ModifierSource().Modify(direct.StatSet.Power)
                .Where(first)
                .Where(second)
                .Add(5f);

            Assert.Equal(10f, direct.StatSet.Power.FinalValue);
            second.Value = true;
            Assert.Equal(15f, direct.StatSet.Power.FinalValue);
            first.Value = false;
            Assert.Equal(10f, direct.StatSet.Power.FinalValue);

            var quick = new TestTag("Item.Quick");
            var groupTarget = new TestSubject(quick);
            var groupCondition = new ReactiveProperty<bool>(true);
            new ModifierSource().For(new StatSubjectGroup().Add(groupTarget))
                .WhereTargetHas(quick)
                .Where(groupCondition)
                .Modify(TestStatSet.PowerKey)
                .Add(5f);

            Assert.Equal(15f, groupTarget.StatSet.Power.FinalValue);
            groupCondition.Value = false;
            Assert.Equal(10f, groupTarget.StatSet.Power.FinalValue);
        }

        [Fact]
        public void GroupR3RuleTracksFutureJoiningAndLeavingMembers()
        {
            var group = new StatSubjectGroup();
            var source = new ModifierSource();
            var enabled = new ReactiveProperty<bool>(false);
            var value = new ReactiveProperty<float>(5f);
            source.For(group).Where(enabled).Modify(TestStatSet.PowerKey).Add(value);
            var subject = new TestSubject();

            group.Add(subject);
            Assert.Equal(10f, subject.StatSet.Power.FinalValue);
            enabled.Value = true;
            Assert.Equal(15f, subject.StatSet.Power.FinalValue);
            group.Remove(subject);
            Assert.Equal(10f, subject.StatSet.Power.FinalValue);
            value.Value = 8f;
            Assert.Equal(10f, subject.StatSet.Power.FinalValue);
            group.Add(subject);
            Assert.Equal(18f, subject.StatSet.Power.FinalValue);
        }

        [Fact]
        public void ModifierAdapterAcceptsCurrentValuePropertiesButNotPlainObservables()
        {
            MethodInfo[] methods = typeof(StatsR3ModifierExtensions).Assembly
                .GetExportedTypes()
                .Where(type => type.Namespace == typeof(StatsR3ModifierExtensions).Namespace)
                .SelectMany(type => type.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .ToArray();

            Assert.Contains(methods.SelectMany(method => method.GetParameters()), parameter =>
                parameter.ParameterType.IsGenericType
                && parameter.ParameterType.GetGenericTypeDefinition() == typeof(ReadOnlyReactiveProperty<>));
            Assert.DoesNotContain(methods.SelectMany(method => method.GetParameters()), parameter =>
                parameter.ParameterType.IsGenericType
                && parameter.ParameterType.GetGenericTypeDefinition() == typeof(Observable<>));
        }

        [Fact]
        public void WrongThreadR3ConditionNotificationsLeaveDirectAndGroupEffectsUnchanged()
        {
            var direct = new TestSubject();
            var directCondition = new ReactiveProperty<bool>(true);
            new ModifierSource().Modify(direct.StatSet.Power).Where(directCondition).Add(5f);
            var groupTarget = new TestSubject();
            var groupCondition = new ReactiveProperty<bool>(true);
            new ModifierSource().For(new StatSubjectGroup().Add(groupTarget))
                .Where(groupCondition)
                .Modify(TestStatSet.PowerKey)
                .Add(5f);

            var directThread = new System.Threading.Thread(() =>
            {
                try { directCondition.Value = false; }
                catch (InvalidOperationException) { }
            });
            directThread.Start();
            directThread.Join();

            var groupThread = new System.Threading.Thread(() =>
            {
                try { groupCondition.Value = false; }
                catch (InvalidOperationException) { }
            });
            groupThread.Start();
            groupThread.Join();

            Assert.Equal(15f, direct.StatSet.Power.FinalValue);
            Assert.Equal(15f, groupTarget.StatSet.Power.FinalValue);
            direct.StatSet.Power.BaseValue = 20f;
            groupTarget.StatSet.Power.BaseValue = 20f;
            Assert.Equal(25f, direct.StatSet.Power.FinalValue);
            Assert.Equal(25f, groupTarget.StatSet.Power.FinalValue);
        }

        private sealed class TestSubject : StatSubject<TestStatSet>
        {
            internal TestSubject(params IGameplayTag[] tags)
                : base(new TestStatSet(), tags)
            {
            }
        }

        private sealed class TestTag : IHierarchicalGameplayTag
        {
            internal TestTag(string path) => Path = path;

            public string Path { get; }

            public bool IsSameOrDescendantOf(IGameplayTag tag) => ReferenceEquals(this, tag);
        }

        private sealed class TestStatSet : StatSet
        {
            internal static readonly StatKey<Stat> PowerKey = CreateKey(
                typeof(TestStatSet),
                nameof(Power),
                statSet => ((TestStatSet)statSet).Power);

            internal static readonly StatKey<RangeStat> AttackKey = CreateKey(
                typeof(TestStatSet),
                nameof(Attack),
                statSet => ((TestStatSet)statSet).Attack);

            internal TestStatSet()
            {
                Power = new Stat(10f);
                Attack = new RangeStat(5f, 10f);
                Health = new Resource(20f);
            }

            public Stat Power { get; }

            public RangeStat Attack { get; }

            public Resource Health { get; }

            protected override void AppendGeneratedMembers(ICollection<StatMemberDescriptor> members)
            {
                members.Add(CreateMember(nameof(Power), StatMemberKind.Stat, statSet => ((TestStatSet)statSet).Power));
                members.Add(CreateMember(nameof(Attack), StatMemberKind.RangeStat, statSet => ((TestStatSet)statSet).Attack));
                members.Add(CreateMember(nameof(Health), StatMemberKind.Resource, statSet => ((TestStatSet)statSet).Health));
            }
        }
    }
}
