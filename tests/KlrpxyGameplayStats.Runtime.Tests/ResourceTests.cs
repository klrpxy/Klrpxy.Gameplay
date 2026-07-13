using System;
using Klrpxy.Gameplay.Stats;
using Xunit;

namespace KlrpxyGameplayStats.Runtime.Tests
{
    public sealed class ResourceTests
    {
        [Fact]
        public void NewResourceStartsWithProvidedValue()
        {
            // 验证新建 Resource 以构造参数作为初始 Value。
            var resource = new Resource(100f);

            Assert.Equal(100f, resource.Value);
        }

        [Fact]
        public void NewResourceKeepsProvidedValueBeforeAnyModification()
        {
            // 验证新建 Resource 在任何修改前保留提供的初始 Value。
            var resource = new Resource(100.9f, RoundingMode.Floor);

            Assert.Equal(100.9f, resource.Value);
        }

        [Fact]
        public void DeclaringBoundsDoesNotRoundInitialValue()
        {
            // 验证声明边界只钳制初始 Value，不应用修改时的取整规则。
            var resource = new Resource(100.9f, RoundingMode.Floor)
                .WithBounds(0f, 200f);

            Assert.Equal(100.9f, resource.Value);
        }

        [Fact]
        public void SetRejectsNonFiniteValue()
        {
            // 验证 Set 拒绝 NaN 和无穷值。
            var resource = new Resource(100f);

            Assert.Throws<ArgumentOutOfRangeException>(() => resource.Set(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => resource.Set(float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => resource.Set(float.NegativeInfinity));
        }

        [Fact]
        public void ConstructorRejectsNonFiniteValue()
        {
            // 验证构造 Resource 时拒绝非有限初始值。
            Assert.Throws<ArgumentOutOfRangeException>(() => new Resource(float.NaN));
        }

        [Fact]
        public void IncreaseAddsAmountToValue()
        {
            // 验证 Increase 将指定数值加到 Resource Value。
            var resource = new Resource(100f);

            resource.Increase(25f);

            Assert.Equal(125f, resource.Value);
        }

        [Fact]
        public void DecreaseSubtractsAmountFromValue()
        {
            // 验证 Decrease 将指定数值从 Resource Value 中减去。
            var resource = new Resource(100f);

            resource.Decrease(25f);

            Assert.Equal(75f, resource.Value);
        }

        [Fact]
        public void SetAppliesConfiguredRoundingRule()
        {
            // 验证 Set 先按 Resource 的取整规则处理候选值。
            var resource = new Resource(100f, rounding: RoundingMode.Floor);

            resource.Set(70.9f);

            Assert.Equal(70f, resource.Value);
        }

        [Fact]
        public void WithBoundsClampLimitsCurrentAndFutureValues()
        {
            // 验证 Clamp 边界立即限制当前 Value，并限制后续修改。
            var resource = new Resource(120f)
                .WithBounds(0f, 100f);

            Assert.Equal(100f, resource.Value);

            resource.Decrease(150f);

            Assert.Equal(0f, resource.Value);
        }

        [Fact]
        public void WithMinimumLimitsValueWithoutAnUpperBound()
        {
            // 验证 WithMinimum 表达没有上限的固定下界。
            var resource = new Resource(5f)
                .WithMinimum(0f);

            resource.Decrease(10f);
            resource.Increase(1000f);

            Assert.Equal(1000f, resource.Value);
        }

        [Fact]
        public void DeclaringBoundsMoreThanOnceIsRejected()
        {
            // 验证 Resource 的永久边界只能声明一次。
            var resource = new Resource(80f)
                .WithBounds(0f, 100f);

            Assert.Throws<InvalidOperationException>(() => resource.WithMinimum(0f));
        }

        [Fact]
        public void SetRoundsCandidateBeforeApplyingBounds()
        {
            // 验证 Set 先取整候选值，再应用 Resource 固有边界。
            var resource = new Resource(5f, RoundingMode.Floor)
                .WithBounds(1.5f, 10f);

            resource.Set(1.9f);

            Assert.Equal(1.5f, resource.Value);
        }
    }
}
