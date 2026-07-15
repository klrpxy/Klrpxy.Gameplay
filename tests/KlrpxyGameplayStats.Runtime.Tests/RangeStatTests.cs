using Klrpxy.Gameplay.Stats;
using Xunit;

namespace KlrpxyGameplayStats.Runtime.Tests
{
    public sealed class RangeStatTests
    {
        [Fact]
        public void NewRangeStatStartsWithProvidedBaseAndFinalRange()
        {
            // 验证新建 RangeStat 的基础区间和最终区间都来自构造参数。
            var stat = new RangeStat(10f, 15f);

            Assert.Equal(
                (10f, 15f, 10f, 15f),
                (stat.BaseRange.Min, stat.BaseRange.Max, stat.FinalRange.Min, stat.FinalRange.Max));
        }

        [Fact]
        public void RangeOverrideReplacesArithmeticResultAndSortsEndpoints()
        {
            // 验证 RangeStat 的完整范围 Override 会覆盖算术结果并保持 Min 不大于 Max。
            var subject = new RangeTestSubject(new RangeTestStatSet());
            var source = new ModifierSource();

            subject.AddModifier(
                Modifier.Override(new FloatRange(40f, 20f), RangeTestStatSet.DamageKey),
                source);

            Assert.Equal((20f, 40f), (subject.StatSet.Damage.FinalRange.Min, subject.StatSet.Damage.FinalRange.Max));
        }

        [Fact]
        public void RangeArithmeticAndClampApplyToBothEndpoints()
        {
            // 验证 RangeStat 两端共享固定算术阶段，Clamp 可把区间收缩为确定值。
            var subject = new RangeTestSubject(new RangeTestStatSet());
            var source = new ModifierSource();

            subject.AddModifier(Modifier.Flat(5f, RangeTestStatSet.DamageKey), source);
            subject.AddModifier(Modifier.Percent(50f, RangeTestStatSet.DamageKey), source);
            subject.AddModifier(Modifier.Multiply(2f, RangeTestStatSet.DamageKey), source);
            subject.AddModifier(Modifier.Clamp(30f, 40f, RangeTestStatSet.DamageKey), source);

            Assert.Equal((40f, 40f), (subject.StatSet.Damage.FinalRange.Min, subject.StatSet.Damage.FinalRange.Max));
        }

        [Fact]
        public void RangeRoundingAndIntrinsicBoundsApplyAfterOverride()
        {
            // 验证 RangeStat 在 Override 后对两端取整并由固有边界收缩为合法区间。
            var subject = new RoundedRangeTestSubject(new RoundedRangeTestStatSet());
            var source = new ModifierSource();

            subject.AddModifier(
                Modifier.Override(new FloatRange(20.8f, 10.2f), RoundedRangeTestStatSet.ValueKey),
                source);

            Assert.Equal((12f, 18f), (subject.StatSet.Value.FinalRange.Min, subject.StatSet.Value.FinalRange.Max));
        }

        [Fact]
        public void DynamicRangeBoundsTrackDeclaredInputs()
        {
            // 验证 RangeStat 动态边界随 ValueInput 变化并立即更新 FinalRange。
            var minimum = new ObservableValue(0f);
            var maximum = new Stat(20f);
            var range = new RangeStat(10f, 30f)
                .WithBounds(ValueInput.External(minimum), ValueInput.Final(maximum));

            maximum.BaseValue = 15f;

            Assert.Equal((10f, 15f), (range.FinalRange.Min, range.FinalRange.Max));
        }

        [Fact]
        public void FinalRangeInputPropagatesBeforeRangeEventIsPublished()
        {
            // 验证 Range Final 输入会传播到目标，且 Range 公开事件观察到稳定后的整张图。
            var rangeSubject = new RangeTestSubject(new RangeTestStatSet());
            var target = new TestSubject(new TestStatSet());
            var source = new ModifierSource();
            target.AddModifier(
                Modifier.Flat(
                    ModifierValue.From(ValueInput.Final(rangeSubject.StatSet.Damage), range => range.Max),
                    TestStatSet.HealthKey),
                source);
            var observedTarget = -1f;
            rangeSubject.StatSet.Damage.OnFinalRangeChanged += (previous, current) =>
                observedTarget = target.StatSet.Health.FinalValue;

            rangeSubject.AddModifier(Modifier.Flat(5f, RangeTestStatSet.DamageKey), source);

            Assert.Equal(120f, observedTarget);
        }
    }

    public sealed class RangeTestStatSet : StatSet
    {
        public static readonly StatKey<RangeStat> DamageKey = CreateKey<RangeStat>(
            typeof(RangeTestStatSet),
            "Tests::RangeTestStatSet.Damage",
            statSet => ((RangeTestStatSet)statSet).Damage);

        public RangeStat Damage { get; } = new RangeStat(10f, 15f);

        protected override void AppendGeneratedMembers(System.Collections.Generic.ICollection<StatMemberDescriptor> members)
        {
            members.Add(CreateMember(
                "Tests::RangeTestStatSet.Damage",
                StatMemberKind.RangeStat,
                statSet => ((RangeTestStatSet)statSet).Damage));
        }
    }

    public sealed class RangeTestSubject : StatSubject<RangeTestStatSet>
    {
        public RangeTestSubject(RangeTestStatSet statSet)
            : base(statSet)
        {
        }
    }

    public sealed class RoundedRangeTestStatSet : StatSet
    {
        public static readonly StatKey<RangeStat> ValueKey = CreateKey<RangeStat>(
            typeof(RoundedRangeTestStatSet),
            "Tests::RoundedRangeTestStatSet.Value",
            statSet => ((RoundedRangeTestStatSet)statSet).Value);

        public RangeStat Value { get; } = new RangeStat(10f, 15f, RoundingMode.Floor).WithBounds(12f, 18f);
    }

    public sealed class RoundedRangeTestSubject : StatSubject<RoundedRangeTestStatSet>
    {
        public RoundedRangeTestSubject(RoundedRangeTestStatSet statSet)
            : base(statSet)
        {
        }
    }
}
