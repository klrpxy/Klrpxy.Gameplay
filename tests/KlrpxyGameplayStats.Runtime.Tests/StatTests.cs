using System;
using Klrpxy.Gameplay.Stats;
using Xunit;

namespace KlrpxyGameplayStats.Runtime.Tests
{
    public sealed class StatTests
    {
        [Fact]
        public void NewStatStartsWithBaseValueAsFinalValue()
        {
            // 验证新建 Stat 在没有 Modifier 时以 BaseValue 作为 FinalValue。
            var stat = new Stat(100f);

            Assert.Equal(100f, stat.FinalValue);
        }

        [Fact]
        public void ChangingBaseValueUpdatesFinalValue()
        {
            // 验证修改 BaseValue 会立即更新没有 Modifier 的 FinalValue。
            var stat = new Stat(100f);

            stat.BaseValue = 125f;

            Assert.Equal(125f, stat.FinalValue);
        }

        [Fact]
        public void FlatModifierUpdatesFinalValueThroughGeneratedKey()
        {
            // 验证 Modifier 通过生成的 StatKey 挂载后会立即改变 FinalValue。
            var owner = new TestOwner(new TestStatSet());
            var source = new ModifierSource();

            owner.AddModifier(Modifier.Flat(25f, TestStatSet.HealthKey), source);

            Assert.Equal(125f, owner.StatSet.Health.FinalValue);
        }

        [Fact]
        public void ArithmeticModifiersFollowTheFixedCalculationOrder()
        {
            // 验证 Flat、Percent 与 Multiply 按固定阶段组合，而非按添加顺序计算。
            var owner = new TestOwner(new TestStatSet());
            var source = new ModifierSource();

            owner.AddModifier(Modifier.Multiply(2f, TestStatSet.HealthKey), source);
            owner.AddModifier(Modifier.Percent(50f, TestStatSet.HealthKey), source);
            owner.AddModifier(Modifier.Flat(10f, TestStatSet.HealthKey), source);

            Assert.Equal(330f, owner.StatSet.Health.FinalValue);
        }

        [Fact]
        public void RemovingWinningOverrideFallsBackToTheNextRule()
        {
            // 验证移除高优先级 Override 后会自动回退到下一条有效规则。
            var owner = new TestOwner(new TestStatSet());
            var source = new ModifierSource();

            owner.AddModifier(Modifier.Override(120f, TestStatSet.HealthKey), source);
            ModifierHandle winner = owner.AddModifier(
                Modifier.Override(150f, TestStatSet.HealthKey, priority: 1),
                source);

            winner.Dispose();

            Assert.Equal(120f, owner.StatSet.Health.FinalValue);
        }

        [Fact]
        public void RoundingAndIntrinsicBoundsApplyAfterTemporaryClamp()
        {
            // 验证 Override 后依次取整、应用临时 Clamp，最后由固有边界决定 FinalValue。
            var owner = new RoundedTestOwner(new RoundedTestStatSet());
            var source = new ModifierSource();

            owner.AddModifier(Modifier.Override(17.8f, RoundedTestStatSet.ValueKey), source);
            owner.AddModifier(Modifier.Clamp(0f, 16f, RoundedTestStatSet.ValueKey), source);

            Assert.Equal(15f, owner.StatSet.Value.FinalValue);
        }

        [Fact]
        public void SourceRemovalRestoresValueAndDisposedSourceRejectsRegistration()
        {
            // 验证 Source 批量移除会恢复数值，且已 Dispose 的 Source 在改变数值前拒绝注册。
            var owner = new TestOwner(new TestStatSet());
            var source = new ModifierSource();

            source.RemoveAllModifiers();
            owner.AddModifier(Modifier.Flat(25f, TestStatSet.HealthKey), source);
            source.RemoveAllModifiers();
            source.Dispose();

            Assert.Equal(100f, owner.StatSet.Health.FinalValue);
            Assert.Throws<ObjectDisposedException>(() =>
                owner.AddModifier(Modifier.Flat(50f, TestStatSet.HealthKey), source));
            Assert.Equal(100f, owner.StatSet.Health.FinalValue);
        }

        [Fact]
        public void FinalValueEventReportsOnlyActualDirectChanges()
        {
            // 验证 FinalValue 仅在直接计算结果实际变化时报告旧值和新值。
            var stat = new Stat(100f);
            var changes = new System.Collections.Generic.List<(float Previous, float Current)>();
            stat.OnFinalValueChanged += (previous, current) => changes.Add((previous, current));

            stat.BaseValue = 100f;
            stat.BaseValue = 125f;

            Assert.Equal(new[] { (100f, 125f) }, changes);
        }

        [Fact]
        public void StatRejectsNonFiniteBaseValues()
        {
            // 验证 Stat 构造和修改 BaseValue 时都会拒绝非有限数值。
            var stat = new Stat(100f);

            Assert.Throws<ArgumentOutOfRangeException>(() => new Stat(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => stat.BaseValue = float.PositiveInfinity);
            Assert.Equal(100f, stat.FinalValue);
        }

        [Fact]
        public void DisposingSourceRejectsRegistrationFromRemovalEvent()
        {
            // 验证 Source Dispose 期间的数值事件不能借由回调重新注册 Modifier。
            var owner = new TestOwner(new TestStatSet());
            var source = new ModifierSource();
            var rejected = false;
            owner.AddModifier(Modifier.Flat(25f, TestStatSet.HealthKey), source);
            owner.StatSet.Health.OnFinalValueChanged += (previous, current) =>
            {
                if (current == 100f)
                {
                    rejected = Assert.Throws<ObjectDisposedException>(() =>
                        owner.AddModifier(Modifier.Flat(10f, TestStatSet.HealthKey), source)) != null;
                }
            };

            source.Dispose();

            Assert.True(rejected);
            Assert.Equal(100f, owner.StatSet.Health.FinalValue);
        }

        [Fact]
        public void RemovingAllSourceModifiersPublishesOnlyTheFinalValue()
        {
            // 验证批量移除同一 Source 的规则时不会向监听者公开中间数值。
            var owner = new TestOwner(new TestStatSet());
            var source = new ModifierSource();
            var changes = new System.Collections.Generic.List<(float Previous, float Current)>();
            owner.AddModifier(Modifier.Flat(10f, TestStatSet.HealthKey), source);
            owner.AddModifier(Modifier.Flat(20f, TestStatSet.HealthKey), source);
            owner.StatSet.Health.OnFinalValueChanged += (previous, current) => changes.Add((previous, current));

            source.RemoveAllModifiers();

            Assert.Equal(new[] { (130f, 100f) }, changes);
        }

    }

    public sealed partial class TestStatSet : StatSet
    {
        public static readonly StatKey<Stat> HealthKey = CreateKey<Stat>(
            typeof(TestStatSet),
            "Tests::TestStatSet.Health",
            statSet => ((TestStatSet)statSet).Health);

        public Stat Health { get; } = new Stat(100f);
    }

    public sealed class TestOwner : StatsOwner<TestStatSet>
    {
        public TestOwner(TestStatSet statSet)
            : base(statSet)
        {
        }
    }

    public sealed partial class RoundedTestStatSet : StatSet
    {
        public static readonly StatKey<Stat> ValueKey = CreateKey<Stat>(
            typeof(RoundedTestStatSet),
            "Tests::RoundedTestStatSet.Value",
            statSet => ((RoundedTestStatSet)statSet).Value);

        public Stat Value { get; } = new Stat(10.5f, RoundingMode.Floor).WithBounds(0f, 15.5f);
    }

    public sealed class RoundedTestOwner : StatsOwner<RoundedTestStatSet>
    {
        public RoundedTestOwner(RoundedTestStatSet statSet)
            : base(statSet)
        {
        }
    }
}
