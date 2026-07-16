using System.Collections.Generic;
using Klrpxy.Gameplay.Stats;
using Xunit;

namespace KlrpxyGameplayStats.Runtime.Tests
{
    public sealed class DynamicModifierFluentTests
    {
        [Fact]
        public void StatInputUsesFinalValueAndPropagatesStableChanges()
        {
            var input = new DynamicSubject(new DynamicStatSet(10f));
            var target = new DynamicSubject(new DynamicStatSet(100f));
            var source = new ModifierSource();
            var changes = new List<(float Previous, float Current)>();
            target.StatSet.Value.OnFinalValueChanged += (previous, current) => changes.Add((previous, current));

            source.Modify(target.StatSet.Value).Add(input.StatSet.Value, value => value * 0.5f);

            Assert.Equal(105f, target.StatSet.Value.FinalValue);
            input.StatSet.Value.BaseValue = 20f;
            Assert.Equal(110f, target.StatSet.Value.FinalValue);
            Assert.Equal(new[] { (100f, 105f), (105f, 110f) }, changes);
        }

        [Fact]
        public void RangeAndResourceInputsUseTheirGameplayValuesAndPropagateChanges()
        {
            var input = new DynamicSubject(new DynamicStatSet(10f));
            var rangeSource = new ModifierSource();
            var modifierSource = new ModifierSource();
            var rangeTarget = new DynamicSubject(new DynamicStatSet(100f));
            var resourceTarget = new DynamicSubject(new DynamicStatSet(100f));

            modifierSource.Modify(rangeTarget.StatSet.Value).Add(input.StatSet.Range, range => range.Max);
            modifierSource.Modify(resourceTarget.StatSet.Value).Add(input.StatSet.Resource, value => value * 2f);

            Assert.Equal(110f, rangeTarget.StatSet.Value.FinalValue);
            Assert.Equal(120f, resourceTarget.StatSet.Value.FinalValue);

            var rangeGroup = new StatSubjectGroup().Add(input);
            rangeSource.For(rangeGroup).Modify(DynamicStatSet.RangeKey).Add(5f);
            input.StatSet.Resource.Set(20f);

            Assert.Equal(115f, rangeTarget.StatSet.Value.FinalValue);
            Assert.Equal(140f, resourceTarget.StatSet.Value.FinalValue);
        }

        [Fact]
        public void SingleInputSupportsAllFourDynamicOperations()
        {
            var input = new DynamicSubject(new DynamicStatSet(10f));
            var addPercentTarget = new DynamicSubject(new DynamicStatSet(100f));
            var multiplyTarget = new DynamicSubject(new DynamicStatSet(100f));
            var overrideTarget = new DynamicSubject(new DynamicStatSet(100f));
            var source = new ModifierSource();

            source.Modify(addPercentTarget.StatSet.Value).AddPercent(input.StatSet.Value, value => value);
            source.Modify(multiplyTarget.StatSet.Value).Multiply(input.StatSet.Value, value => value * 0.2f);
            source.Modify(overrideTarget.StatSet.Value).Override(200f, priority: 6);
            source.Modify(overrideTarget.StatSet.Value).Override(input.StatSet.Value, value => value + 40f, priority: 7);

            Assert.Equal(110f, addPercentTarget.StatSet.Value.FinalValue);
            Assert.Equal(200f, multiplyTarget.StatSet.Value.FinalValue);
            Assert.Equal(50f, overrideTarget.StatSet.Value.FinalValue);
        }

        [Fact]
        public void RuntimeSelectorFailureKeepsLastValueReportsAndRetries()
        {
            var input = new DynamicSubject(new DynamicStatSet(10f));
            var target = new DynamicSubject(new DynamicStatSet(100f));
            var diagnostics = new List<System.Exception>();
            var changes = new List<(float Previous, float Current)>();
            System.Action<System.Exception> previousHandler = StatsDiagnostics.EventExceptionHandler;
            StatsDiagnostics.EventExceptionHandler = diagnostics.Add;
            target.StatSet.Value.OnFinalValueChanged += (previous, current) => changes.Add((previous, current));

            try
            {
                new ModifierSource().Modify(target.StatSet.Value).Add(
                    input.StatSet.Value,
                    value => value == 20f
                        ? throw new System.InvalidOperationException("temporary selector failure")
                        : value == 30f ? float.NaN : value);

                input.StatSet.Value.BaseValue = 20f;
                Assert.Equal(110f, target.StatSet.Value.FinalValue);
                input.StatSet.Value.BaseValue = 30f;
                Assert.Equal(110f, target.StatSet.Value.FinalValue);
                input.StatSet.Value.BaseValue = 40f;

                Assert.Equal(140f, target.StatSet.Value.FinalValue);
                Assert.Equal(2, diagnostics.Count);
                Assert.IsType<System.InvalidOperationException>(diagnostics[0]);
                Assert.IsType<System.ArgumentOutOfRangeException>(diagnostics[1]);
                Assert.Equal(new[] { (100f, 110f), (110f, 140f) }, changes);
            }
            finally
            {
                StatsDiagnostics.EventExceptionHandler = previousHandler;
            }
        }

        [Fact]
        public void InitialSelectorFailureIsAtomicAndTheBuilderCanRetry()
        {
            var input = new DynamicSubject(new DynamicStatSet(10f));
            var target = new DynamicSubject(new DynamicStatSet(100f));
            var source = new ModifierSource();
            StatModifierBuilder builder = source.Modify(target.StatSet.Value);
            var changes = new List<(float Previous, float Current)>();
            target.StatSet.Value.OnFinalValueChanged += (previous, current) => changes.Add((previous, current));

            Assert.Throws<System.InvalidOperationException>(() =>
                builder.Add(input.StatSet.Value, value => throw new System.InvalidOperationException("initial failure")));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                builder.Add(input.StatSet.Value, value => float.PositiveInfinity));

            ModifierHandle handle = builder.Add(input.StatSet.Value, value => value);
            Assert.Equal(110f, target.StatSet.Value.FinalValue);
            handle.Dispose();
            Assert.Equal(100f, target.StatSet.Value.FinalValue);
            Assert.Equal(new[] { (100f, 110f), (110f, 100f) }, changes);
        }

        [Fact]
        public void CyclicStatRangeAndResourceInputsAreRejectedAtomically()
        {
            var target = new DynamicSubject(new DynamicStatSet(100f));
            var source = new ModifierSource();
            StatModifierBuilder builder = source.Modify(target.StatSet.Value);
            target.StatSet.Range.WithBounds(ValueInput.Base(target.StatSet.Value), ValueInput.Final(target.StatSet.Value));
            target.StatSet.Resource.WithBounds(0f, ValueInput.Final(target.StatSet.Value));

            Assert.Throws<System.InvalidOperationException>(() =>
                builder.Add(target.StatSet.Value, value => value));
            Assert.Throws<System.InvalidOperationException>(() =>
                builder.Add(target.StatSet.Range, range => range.Max));
            Assert.Throws<System.InvalidOperationException>(() =>
                builder.Add(target.StatSet.Resource, value => value));
            Assert.Equal(100f, target.StatSet.Value.FinalValue);

            ModifierHandle retry = builder.Add(5f);
            Assert.Equal(105f, target.StatSet.Value.FinalValue);
            retry.Dispose();
            Assert.Equal(100f, target.StatSet.Value.FinalValue);
        }

        [Fact]
        public void HandleAndSourceEndDynamicRegistrationsIndependently()
        {
            var input = new DynamicSubject(new DynamicStatSet(10f));
            var target = new DynamicSubject(new DynamicStatSet(100f));
            var firstSource = new ModifierSource();
            ModifierHandle handle = firstSource.Modify(target.StatSet.Value).Add(input.StatSet.Value, value => value);

            handle.Dispose();
            input.StatSet.Value.BaseValue = 20f;
            Assert.Equal(100f, target.StatSet.Value.FinalValue);

            var secondSource = new ModifierSource();
            secondSource.Modify(target.StatSet.Value).Add(input.StatSet.Value, value => value);
            Assert.Equal(120f, target.StatSet.Value.FinalValue);
            secondSource.Dispose();
            input.StatSet.Value.BaseValue = 30f;
            Assert.Equal(100f, target.StatSet.Value.FinalValue);
        }

        private sealed class DynamicStatSet : StatSet
        {
            public DynamicStatSet(float value)
            {
                Value = new Stat(value);
                Range = new RangeStat(5f, 10f);
                Resource = new Resource(10f);
            }

            public static readonly StatKey<RangeStat> RangeKey = CreateKey<RangeStat>(
                typeof(DynamicStatSet),
                "Tests::DynamicStatSet.Range",
                statSet => ((DynamicStatSet)statSet).Range);

            public Stat Value { get; }

            public RangeStat Range { get; }

            public Resource Resource { get; }

            protected override void AppendGeneratedMembers(ICollection<StatMemberDescriptor> members)
            {
                members.Add(CreateMember<Stat>(
                    "Tests::DynamicStatSet.Value",
                    StatMemberKind.Stat,
                    statSet => ((DynamicStatSet)statSet).Value));
                members.Add(CreateMember<RangeStat>(
                    "Tests::DynamicStatSet.Range",
                    StatMemberKind.RangeStat,
                    statSet => ((DynamicStatSet)statSet).Range));
                members.Add(CreateMember<Resource>(
                    "Tests::DynamicStatSet.Resource",
                    StatMemberKind.Resource,
                    statSet => ((DynamicStatSet)statSet).Resource));
            }
        }

        private sealed class DynamicSubject : StatSubject<DynamicStatSet>
        {
            public DynamicSubject(DynamicStatSet statSet)
                : base(statSet)
            {
            }
        }
    }
}
