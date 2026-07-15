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
            var subject = new TestSubject(new TestStatSet());
            var source = new ModifierSource();

            subject.AddModifier(Modifier.Flat(25f, TestStatSet.HealthKey), source);

            Assert.Equal(125f, subject.StatSet.Health.FinalValue);
        }

        [Fact]
        public void ArithmeticModifiersFollowTheFixedCalculationOrder()
        {
            // 验证 Flat、Percent 与 Multiply 按固定阶段组合，而非按添加顺序计算。
            var subject = new TestSubject(new TestStatSet());
            var source = new ModifierSource();

            subject.AddModifier(Modifier.Multiply(2f, TestStatSet.HealthKey), source);
            subject.AddModifier(Modifier.Percent(50f, TestStatSet.HealthKey), source);
            subject.AddModifier(Modifier.Flat(10f, TestStatSet.HealthKey), source);

            Assert.Equal(330f, subject.StatSet.Health.FinalValue);
        }

        [Fact]
        public void RemovingWinningOverrideFallsBackToTheNextRule()
        {
            // 验证移除高优先级 Override 后会自动回退到下一条有效规则。
            var subject = new TestSubject(new TestStatSet());
            var source = new ModifierSource();

            subject.AddModifier(Modifier.Override(120f, TestStatSet.HealthKey), source);
            ModifierHandle winner = subject.AddModifier(
                Modifier.Override(150f, TestStatSet.HealthKey, priority: 1),
                source);

            winner.Dispose();

            Assert.Equal(120f, subject.StatSet.Health.FinalValue);
        }

        [Fact]
        public void RoundingAndIntrinsicBoundsApplyAfterTemporaryClamp()
        {
            // 验证 Override 后依次取整、应用临时 Clamp，最后由固有边界决定 FinalValue。
            var subject = new RoundedTestSubject(new RoundedTestStatSet());
            var source = new ModifierSource();

            subject.AddModifier(Modifier.Override(17.8f, RoundedTestStatSet.ValueKey), source);
            subject.AddModifier(Modifier.Clamp(0f, 16f, RoundedTestStatSet.ValueKey), source);

            Assert.Equal(15f, subject.StatSet.Value.FinalValue);
        }

        [Fact]
        public void SourceRemovalRestoresValueAndDisposedSourceRejectsRegistration()
        {
            // 验证 Source 批量移除会恢复数值，且已 Dispose 的 Source 在改变数值前拒绝注册。
            var subject = new TestSubject(new TestStatSet());
            var source = new ModifierSource();

            source.RemoveAllModifiers();
            subject.AddModifier(Modifier.Flat(25f, TestStatSet.HealthKey), source);
            source.RemoveAllModifiers();
            source.Dispose();

            Assert.Equal(100f, subject.StatSet.Health.FinalValue);
            Assert.Throws<ObjectDisposedException>(() =>
                subject.AddModifier(Modifier.Flat(50f, TestStatSet.HealthKey), source));
            Assert.Equal(100f, subject.StatSet.Health.FinalValue);
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
            var subject = new TestSubject(new TestStatSet());
            var source = new ModifierSource();
            var rejected = false;
            subject.AddModifier(Modifier.Flat(25f, TestStatSet.HealthKey), source);
            subject.StatSet.Health.OnFinalValueChanged += (previous, current) =>
            {
                if (current == 100f)
                {
                    rejected = Assert.Throws<ObjectDisposedException>(() =>
                        subject.AddModifier(Modifier.Flat(10f, TestStatSet.HealthKey), source)) != null;
                }
            };

            source.Dispose();

            Assert.True(rejected);
            Assert.Equal(100f, subject.StatSet.Health.FinalValue);
        }

        [Fact]
        public void RemovingAllSourceModifiersPublishesOnlyTheFinalValue()
        {
            // 验证批量移除同一 Source 的规则时不会向监听者公开中间数值。
            var subject = new TestSubject(new TestStatSet());
            var source = new ModifierSource();
            var changes = new System.Collections.Generic.List<(float Previous, float Current)>();
            subject.AddModifier(Modifier.Flat(10f, TestStatSet.HealthKey), source);
            subject.AddModifier(Modifier.Flat(20f, TestStatSet.HealthKey), source);
            subject.StatSet.Health.OnFinalValueChanged += (previous, current) => changes.Add((previous, current));

            source.RemoveAllModifiers();

            Assert.Equal(new[] { (130f, 100f) }, changes);
        }

        [Fact]
        public void DynamicModifierRecalculatesWhenDeclaredInputChanges()
        {
            // 验证动态 Modifier 会在显式 ValueInput 变化时自动更新目标 FinalValue。
            var subject = new TestSubject(new TestStatSet());
            var source = new ModifierSource();
            var bonus = new Stat(10f);
            ModifierValue value = ModifierValue.From(ValueInput.Final(bonus), input => input * 2f);

            subject.AddModifier(Modifier.Flat(value, TestStatSet.HealthKey), source);
            bonus.BaseValue = 20f;

            Assert.Equal(140f, subject.StatSet.Health.FinalValue);
        }

        [Fact]
        public void DynamicModifierCycleIsRejectedBeforeTargetChanges()
        {
            // 验证会形成 FinalValue 环的动态 Modifier 在改变注册和值前被原子拒绝。
            var first = new TestSubject(new TestStatSet());
            var second = new TestSubject(new TestStatSet());
            var source = new ModifierSource();
            first.AddModifier(
                Modifier.Flat(
                    ModifierValue.From(ValueInput.Final(second.StatSet.Health), value => value),
                    TestStatSet.HealthKey),
                source);

            Assert.Throws<InvalidOperationException>(() => second.AddModifier(
                Modifier.Flat(
                    ModifierValue.From(ValueInput.Final(first.StatSet.Health), value => value),
                    TestStatSet.HealthKey),
                source));

            Assert.Equal(200f, first.StatSet.Health.FinalValue);
            Assert.Equal(100f, second.StatSet.Health.FinalValue);
        }

        [Fact]
        public void DynamicModifierCombinesThreeDeclaredInputsIncludingExternalValue()
        {
            // 验证动态 Modifier 可组合三个显式输入，并响应外部可观察值变化。
            var subject = new TestSubject(new TestStatSet());
            var source = new ModifierSource();
            var first = new Stat(10f);
            var second = new Resource(5f);
            var external = new ObservableValue(2f);
            ModifierValue value = ModifierValue.From(
                ValueInput.Final(first),
                ValueInput.Current(second),
                ValueInput.External(external),
                (a, b, c) => a + b + c);
            subject.AddModifier(Modifier.Flat(value, TestStatSet.HealthKey), source);

            external.Value = 10f;

            Assert.Equal(125f, subject.StatSet.Health.FinalValue);
        }

        [Fact]
        public void DynamicModifierCanReadFinalRangeInput()
        {
            // 验证动态 Modifier 可以读取 RangeStat 的完整 FinalRange。
            var subject = new TestSubject(new TestStatSet());
            var source = new ModifierSource();
            var range = new RangeStat(10f, 25f);
            ModifierValue value = ModifierValue.From(
                ValueInput.Final(range),
                current => current.Max - current.Min);

            subject.AddModifier(Modifier.Flat(value, TestStatSet.HealthKey), source);

            Assert.Equal(115f, subject.StatSet.Health.FinalValue);
        }

        [Fact]
        public void DynamicModifierCombinesRangeAndScalarInputs()
        {
            // 验证 Range Final 可以与其他显式输入组合成动态 ModifierValue。
            var subject = new TestSubject(new TestStatSet());
            var source = new ModifierSource();
            var range = new RangeStat(10f, 25f);
            var bonus = new ObservableValue(5f);
            ModifierValue value = ModifierValue.From(
                ValueInput.Final(range),
                ValueInput.External(bonus),
                (current, extra) => current.Max - current.Min + extra);

            subject.AddModifier(Modifier.Flat(value, TestStatSet.HealthKey), source);
            bonus.Value = 10f;

            Assert.Equal(125f, subject.StatSet.Health.FinalValue);
        }

        [Fact]
        public void MutationFromAnotherGameplayThreadFailsImmediately()
        {
            // 验证从非创建 Gameplay 线程修改 Stat 会立即失败。
            var stat = new Stat(100f);
            Exception exception = null;
            var thread = new System.Threading.Thread(() =>
            {
                try { stat.BaseValue = 50f; }
                catch (Exception caught) { exception = caught; }
            });

            thread.Start();
            thread.Join();

            Assert.IsType<InvalidOperationException>(exception);
            Assert.Equal(100f, stat.FinalValue);
        }

        [Fact]
        public void DiamondDependencyPublishesOneRoundStartToFinalEvent()
        {
            // 验证菱形依赖导致目标多次重算时，同轮只发布一次开始值到最终值事件。
            var input = new Stat(10f);
            var left = new TestSubject(new TestStatSet());
            var right = new TestSubject(new TestStatSet());
            var target = new TestSubject(new TestStatSet());
            var source = new ModifierSource();
            left.AddModifier(Modifier.Flat(ModifierValue.From(ValueInput.Final(input), value => value), TestStatSet.HealthKey), source);
            right.AddModifier(Modifier.Flat(ModifierValue.From(ValueInput.Final(input), value => value), TestStatSet.HealthKey), source);
            target.AddModifier(
                Modifier.Flat(
                    ModifierValue.From(
                        ValueInput.Final(left.StatSet.Health),
                        ValueInput.Final(right.StatSet.Health),
                        (a, b) => a + b),
                    TestStatSet.HealthKey),
                source);
            var changes = new System.Collections.Generic.List<(float Previous, float Current)>();
            target.StatSet.Health.OnFinalValueChanged += (previous, current) => changes.Add((previous, current));

            input.BaseValue = 20f;

            Assert.Equal(new[] { (320f, 340f) }, changes);
        }

        [Fact]
        public void DiamondDependencyReturningToRoundStartPublishesNoEvent()
        {
            // 验证节点在同轮传播结束时回到开始值不会发布变化事件。
            var input = new Stat(10f);
            var left = new TestSubject(new TestStatSet());
            var right = new TestSubject(new TestStatSet());
            var target = new TestSubject(new TestStatSet());
            var source = new ModifierSource();
            left.AddModifier(Modifier.Flat(ModifierValue.From(ValueInput.Final(input), value => value), TestStatSet.HealthKey), source);
            right.AddModifier(Modifier.Flat(ModifierValue.From(ValueInput.Final(input), value => value), TestStatSet.HealthKey), source);
            target.AddModifier(
                Modifier.Flat(
                    ModifierValue.From(
                        ValueInput.Final(left.StatSet.Health),
                        ValueInput.Final(right.StatSet.Health),
                        (a, b) => a - b),
                    TestStatSet.HealthKey),
                source);
            var eventCount = 0;
            target.StatSet.Health.OnFinalValueChanged += (previous, current) => eventCount++;

            input.BaseValue = 20f;

            Assert.Equal(0, eventCount);
            Assert.Equal(100f, target.StatSet.Health.FinalValue);
        }

        [Fact]
        public void ExternalInputChangePublishesAfterEntireGraphRecalculates()
        {
            // 验证外部可观察输入的一次修改只开启一轮传播并公开最终值。
            var input = new ObservableValue(10f);
            var left = new TestSubject(new TestStatSet());
            var right = new TestSubject(new TestStatSet());
            var target = new TestSubject(new TestStatSet());
            var source = new ModifierSource();
            left.AddModifier(Modifier.Flat(ModifierValue.From(ValueInput.External(input), value => value), TestStatSet.HealthKey), source);
            right.AddModifier(Modifier.Flat(ModifierValue.From(ValueInput.External(input), value => value), TestStatSet.HealthKey), source);
            target.AddModifier(
                Modifier.Flat(
                    ModifierValue.From(
                        ValueInput.Final(left.StatSet.Health),
                        ValueInput.Final(right.StatSet.Health),
                        (a, b) => a + b),
                    TestStatSet.HealthKey),
                source);
            var changes = new System.Collections.Generic.List<(float Previous, float Current)>();
            target.StatSet.Health.OnFinalValueChanged += (previous, current) => changes.Add((previous, current));

            input.Value = 20f;

            Assert.Equal(new[] { (320f, 340f) }, changes);
        }

        [Fact]
        public void DynamicStatBoundsTrackDeclaredInputsAndRejectSelfCycle()
        {
            // 验证 Stat 动态边界自动传播，并在绑定前拒绝依赖自身 FinalValue 的环。
            var maximum = new Stat(100f);
            var minimum = new ObservableValue(0f);
            var stat = new Stat(150f).WithBounds(ValueInput.External(minimum), ValueInput.Final(maximum));

            maximum.BaseValue = 80f;

            Assert.Equal(80f, stat.FinalValue);
            var unbounded = new Stat(100f);
            Assert.Throws<InvalidOperationException>(() =>
                unbounded.WithBounds(ValueInput.External(minimum), ValueInput.Final(unbounded)));
            unbounded.BaseValue = 200f;
            Assert.Equal(200f, unbounded.FinalValue);
        }

        [Fact]
        public void RemovingDynamicModifierUnsubscribesInputAndDependencyEdge()
        {
            // 验证移除动态 Modifier 后取消输入订阅并从依赖图删除关系。
            var first = new TestSubject(new TestStatSet());
            var second = new TestSubject(new TestStatSet());
            var source = new ModifierSource();
            ModifierHandle handle = first.AddModifier(
                Modifier.Flat(
                    ModifierValue.From(ValueInput.Final(second.StatSet.Health), value => value),
                    TestStatSet.HealthKey),
                source);

            handle.Dispose();
            second.AddModifier(
                Modifier.Flat(
                    ModifierValue.From(ValueInput.Final(first.StatSet.Health), value => value),
                    TestStatSet.HealthKey),
                source);
            first.StatSet.Health.BaseValue = 120f;

            Assert.Equal(120f, first.StatSet.Health.FinalValue);
            Assert.Equal(220f, second.StatSet.Health.FinalValue);
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

    public sealed class TestSubject : StatSubject<TestStatSet>
    {
        public TestSubject(TestStatSet statSet)
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

    public sealed class RoundedTestSubject : StatSubject<RoundedTestStatSet>
    {
        public RoundedTestSubject(RoundedTestStatSet statSet)
            : base(statSet)
        {
        }
    }
}
