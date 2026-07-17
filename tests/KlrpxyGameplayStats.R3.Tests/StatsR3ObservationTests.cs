using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Klrpxy.Gameplay.Stats.R3;
using R3;
using Xunit;

namespace Klrpxy.Gameplay.Stats.R3.Tests
{
    public sealed class StatsR3ObservationTests
    {
        [Fact]
        public void StatObservationImmediatelyPublishesCurrentFinalValue()
        {
            var subject = new TestSubject();
            var values = new List<float>();

            using (subject.StatSet.Power.ObserveFinalValue().Subscribe(values.Add))
            {
                Assert.Equal(new[] { 10f }, values);
            }
        }

        [Fact]
        public void StatObservationPublishesOnlyActualFinalValueChanges()
        {
            var subject = new TestSubject();
            var values = new List<float>();

            using (subject.StatSet.Power.ObserveFinalValue().Subscribe(values.Add))
            {
                subject.StatSet.Power.BaseValue = 15f;
                subject.StatSet.Power.BaseValue = 15f;
            }

            subject.StatSet.Power.BaseValue = 20f;
            Assert.Equal(new[] { 10f, 15f }, values);
        }

        [Fact]
        public void RangeStatObservationPublishesCurrentAndChangedFinalRanges()
        {
            var subject = new TestSubject();
            var values = new List<FloatRange>();

            using (subject.StatSet.Attack.ObserveFinalRange().Subscribe(values.Add))
            {
                subject.StatSet.Attack.WithMinimum(0f).WithMaximum(6f);
                subject.StatSet.Attack.WithMinimum(0f).WithMaximum(6f);
            }

            Assert.Equal(
                new[] { new FloatRange(5f, 8f), new FloatRange(5f, 6f) },
                values);
        }

        [Fact]
        public void ResourceObservationPublishesCurrentAndChangedValues()
        {
            var subject = new TestSubject();
            var values = new List<float>();

            using (subject.StatSet.Health.ObserveValue().Subscribe(values.Add))
            {
                subject.StatSet.Health.Decrease(5f);
                subject.StatSet.Health.Set(15f);
            }

            Assert.Equal(new[] { 20f, 15f }, values);
        }

        [Fact]
        public void SubjectDisposeSuccessfullyCompletesEveryOwnedObservationWithoutExtraValues()
        {
            var subject = new TestSubject();
            var statValues = new List<float>();
            var rangeValues = new List<FloatRange>();
            var resourceValues = new List<float>();
            var completions = new List<Result>();

            subject.StatSet.Power.ObserveFinalValue().Subscribe(statValues.Add, completions.Add);
            subject.StatSet.Attack.ObserveFinalRange().Subscribe(rangeValues.Add, completions.Add);
            subject.StatSet.Health.ObserveValue().Subscribe(resourceValues.Add, completions.Add);

            subject.Dispose();
            subject.Dispose();

            Assert.Equal(new[] { 10f }, statValues);
            Assert.Equal(new[] { new FloatRange(5f, 8f) }, rangeValues);
            Assert.Equal(new[] { 20f }, resourceValues);
            Assert.Equal(3, completions.Count);
            Assert.All(completions, completion => Assert.True(completion.IsSuccess));
        }

        [Fact]
        public void SubscriberCanEndBeforeSubjectWithoutLaterValuesOrCompletion()
        {
            var subject = new TestSubject();
            var values = new List<float>();
            var completions = new List<Result>();
            var subscription = subject.StatSet.Power
                .ObserveFinalValue()
                .Subscribe(values.Add, completions.Add);

            subscription.Dispose();
            subscription.Dispose();
            subject.StatSet.Power.BaseValue = 15f;
            subject.Dispose();

            Assert.Equal(new[] { 10f }, values);
            Assert.Empty(completions);
        }

        [Fact]
        public void ObservationPublishesOnlyTheStableResultOfOnePropagationRound()
        {
            var subject = new TestSubject();
            var source = new ModifierSource();
            source.Modify(subject.StatSet.Power).Add(5f);
            source.Modify(subject.StatSet.Power).Add(10f);
            var values = new List<float>();
            using (subject.StatSet.Power.ObserveFinalValue().Subscribe(values.Add))
            {
                source.RemoveAllModifiers();
            }

            Assert.Equal(new[] { 25f, 10f }, values);
        }

        [Fact]
        public void AdapterExposesOnlyNormalGameplayResultObservationMethods()
        {
            string[] methods = typeof(StatsR3ObservationExtensions)
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(method => method.Name)
                .OrderBy(name => name)
                .ToArray();

            Assert.Equal(
                new[] { "ObserveFinalRange", "ObserveFinalValue", "ObserveValue" },
                methods);
        }

        [Fact]
        public void ObservationAndSubscriptionDoNotKeepSubjectAlive()
        {
            WeakReference<TestSubject> subjectReference = CreateUnrootedObservation(
                out global::R3.Observable<float> observation,
                out System.IDisposable subscription);

            CollectGarbage();

            Assert.False(subjectReference.TryGetTarget(out TestSubject _));
            subscription.Dispose();
            GC.KeepAlive(observation);
            GC.KeepAlive(subscription);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference<TestSubject> CreateUnrootedObservation(
            out global::R3.Observable<float> observation,
            out System.IDisposable subscription)
        {
            var subject = new TestSubject();
            observation = subject.StatSet.Power.ObserveFinalValue();
            subscription = observation.Subscribe();
            return new WeakReference<TestSubject>(subject);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CollectGarbage()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private sealed class TestSubject : StatSubject<TestStatSet>
        {
            internal TestSubject()
                : base(new TestStatSet())
            {
            }
        }

        private sealed class TestStatSet : StatSet
        {
            internal static readonly StatKey<Stat> PowerKey = CreateKey(
                typeof(TestStatSet),
                nameof(Power),
                statSet => ((TestStatSet)statSet).Power);

            internal TestStatSet()
            {
                Power = new Stat(10f);
                Attack = new RangeStat(5f, 8f);
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
