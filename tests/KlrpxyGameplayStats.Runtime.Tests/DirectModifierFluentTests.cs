using System;
using System.Runtime.CompilerServices;
using Klrpxy.Gameplay.Stats;
using Xunit;

namespace KlrpxyGameplayStats.Runtime.Tests
{
    public sealed class DirectModifierFluentTests
    {
        [Fact]
        public void AddRegistersAgainstTheActualStatAndReturnsARemovableHandle()
        {
            var subject = new DirectSubject(new DirectStatSet());
            var source = new ModifierSource();

            ModifierHandle handle = source.Modify(subject.StatSet.Health).Add(5f);

            Assert.Equal(105f, subject.StatSet.Health.FinalValue);
            handle.Dispose();
            Assert.Equal(100f, subject.StatSet.Health.FinalValue);
        }

        [Fact]
        public void FixedOperationsPreserveCalculationStagesAndOverridePriority()
        {
            var subject = new DirectSubject(new DirectStatSet());
            var source = new ModifierSource();
            StatModifierBuilder health = source.Modify(subject.StatSet.Health);

            health.Multiply(2f);
            health.AddPercent(50f);
            health.Add(10f);
            Assert.Equal(330f, subject.StatSet.Health.FinalValue);

            health.Override(120f);
            health.Override(150f, priority: 1);
            Assert.Equal(150f, subject.StatSet.Health.FinalValue);

            health.Clamp(0f, 140f);
            Assert.Equal(140f, subject.StatSet.Health.FinalValue);
        }

        [Fact]
        public void ReusedBuilderCreatesIndependentHandlesAndSourceRemovesTheRemainder()
        {
            var subject = new DirectSubject(new DirectStatSet());
            var source = new ModifierSource();
            StatModifierBuilder health = source.Modify(subject.StatSet.Health);

            ModifierHandle first = health.Add(5f);
            health.Add(10f);

            first.Dispose();
            first.Dispose();
            Assert.Equal(110f, subject.StatSet.Health.FinalValue);

            source.Dispose();
            source.Dispose();
            Assert.Equal(100f, subject.StatSet.Health.FinalValue);
        }

        [Fact]
        public void InvalidFixedValuesFailBeforePublishingOrRegisteringAnything()
        {
            var subject = new DirectSubject(new DirectStatSet());
            var source = new ModifierSource();
            StatModifierBuilder health = source.Modify(subject.StatSet.Health);
            var changes = new System.Collections.Generic.List<(float Previous, float Current)>();
            subject.StatSet.Health.OnFinalValueChanged += (previous, current) => changes.Add((previous, current));

            Assert.Throws<ArgumentOutOfRangeException>(() => health.Add(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => health.AddPercent(float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => health.Multiply(float.NegativeInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => health.Override(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => health.Clamp(10f, 5f));

            source.Dispose();
            Assert.Equal(100f, subject.StatSet.Health.FinalValue);
            Assert.Empty(changes);
        }

        [Fact]
        public void NullUnboundAndEndedTargetsFailWithoutChangingALiveStat()
        {
            var subject = new DirectSubject(new DirectStatSet());
            var source = new ModifierSource();
            StatModifierBuilder endedSourceBuilder = source.Modify(subject.StatSet.Health);
            source.Dispose();

            Assert.Throws<ArgumentNullException>(() => new ModifierSource().Modify(null).Add(5f));
            Assert.Throws<InvalidOperationException>(() => new ModifierSource().Modify(new Stat(100f)).Add(5f));
            Assert.Throws<ObjectDisposedException>(() => endedSourceBuilder.Add(5f));
            Assert.Equal(100f, subject.StatSet.Health.FinalValue);

            var endedSubject = new DirectSubject(new DirectStatSet());
            StatModifierBuilder endedSubjectBuilder = new ModifierSource().Modify(endedSubject.StatSet.Health);
            endedSubject.Dispose();

            Assert.Throws<ObjectDisposedException>(() => endedSubjectBuilder.Add(5f));
        }

        [Fact]
        public void SuccessfulRegistrationAndRemovalPublishNormalFinalValueEvents()
        {
            var subject = new DirectSubject(new DirectStatSet());
            var source = new ModifierSource();
            var changes = new System.Collections.Generic.List<(float Previous, float Current)>();
            subject.StatSet.Health.OnFinalValueChanged += (previous, current) => changes.Add((previous, current));

            ModifierHandle handle = source.Modify(subject.StatSet.Health).Add(5f);
            handle.Dispose();

            Assert.Equal(new[] { (100f, 105f), (105f, 100f) }, changes);
        }

        [Fact]
        public void FailedCalculationLeavesTheBuilderAndSourceSafeToRetry()
        {
            var subject = new DirectSubject(new DirectStatSet());
            var source = new ModifierSource();
            StatModifierBuilder health = source.Modify(subject.StatSet.Health);

            Assert.Throws<ArgumentOutOfRangeException>(() => health.Multiply(float.MaxValue));

            ModifierHandle retry = health.Add(5f);
            Assert.Equal(105f, subject.StatSet.Health.FinalValue);
            retry.Dispose();
            Assert.Equal(100f, subject.StatSet.Health.FinalValue);
        }

        [Fact]
        public void BuilderDoesNotOwnTheSourceSubjectOrStatLifetime()
        {
            StatModifierBuilder builder = CreateUnownedBuilder(
                out WeakReference source,
                out WeakReference subject,
                out WeakReference stat);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.False(source.IsAlive);
            Assert.False(subject.IsAlive);
            Assert.False(stat.IsAlive);
            Assert.IsNotAssignableFrom<IDisposable>(builder);
            Assert.Throws<ArgumentNullException>(() => builder.Add(5f));
        }

        [Fact]
        public void DownstreamFailureRollsBackAllValuesAndEventsBeforeRetry()
        {
            var input = new DirectSubject(new DirectStatSet());
            var updatedBeforeFailure = new DirectSubject(new DirectStatSet());
            var failing = new DirectSubject(new DirectStatSet());
            var dependencySource = new ModifierSource();
            dependencySource.Modify(updatedBeforeFailure.StatSet.Health)
                .Add(input.StatSet.Health, value => value);
            dependencySource.Modify(failing.StatSet.Health)
                .Multiply(input.StatSet.Health, value => value <= 100f ? 1f : float.MaxValue);
            var inputChanges = new System.Collections.Generic.List<(float Previous, float Current)>();
            var dependentChanges = new System.Collections.Generic.List<(float Previous, float Current)>();
            input.StatSet.Health.OnFinalValueChanged += (previous, current) => inputChanges.Add((previous, current));
            updatedBeforeFailure.StatSet.Health.OnFinalValueChanged +=
                (previous, current) => dependentChanges.Add((previous, current));
            var source = new ModifierSource();
            StatModifierBuilder builder = source.Modify(input.StatSet.Health);

            Assert.Throws<ArgumentOutOfRangeException>(() => builder.Add(5f));

            Assert.Equal(100f, input.StatSet.Health.FinalValue);
            Assert.Equal(200f, updatedBeforeFailure.StatSet.Health.FinalValue);
            Assert.Equal(100f, failing.StatSet.Health.FinalValue);
            Assert.Empty(inputChanges);
            Assert.Empty(dependentChanges);

            builder.Add(-1f);
            Assert.Equal(99f, input.StatSet.Health.FinalValue);
            Assert.Equal(199f, updatedBeforeFailure.StatSet.Health.FinalValue);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static StatModifierBuilder CreateUnownedBuilder(
            out WeakReference sourceReference,
            out WeakReference subjectReference,
            out WeakReference statReference)
        {
            var subject = new DirectSubject(new DirectStatSet());
            var source = new ModifierSource();
            sourceReference = new WeakReference(source);
            subjectReference = new WeakReference(subject);
            statReference = new WeakReference(subject.StatSet.Health);
            return source.Modify(subject.StatSet.Health);
        }

        private sealed class DirectStatSet : StatSet
        {
            public static readonly StatKey<Stat> HealthKey = CreateKey<Stat>(
                typeof(DirectStatSet),
                "Tests::DirectStatSet.Health",
                statSet => ((DirectStatSet)statSet).Health);

            public Stat Health { get; } = new Stat(100f);

            protected override void AppendGeneratedMembers(System.Collections.Generic.ICollection<StatMemberDescriptor> members)
            {
                members.Add(CreateMember<Stat>(
                    "Tests::DirectStatSet.Health",
                    StatMemberKind.Stat,
                    statSet => ((DirectStatSet)statSet).Health));
            }
        }

        private sealed class DirectSubject : StatSubject<DirectStatSet>
        {
            public DirectSubject(DirectStatSet statSet)
                : base(statSet)
            {
            }
        }
    }
}
