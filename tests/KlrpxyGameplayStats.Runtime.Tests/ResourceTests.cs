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
                .WithMinimum(0f)
                .WithMaximum(200f);

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
        public void MinimumAndMaximumClampCurrentAndFutureValues()
        {
            // 验证 Clamp 边界立即限制当前 Value，并限制后续修改。
            var resource = new Resource(120f)
                .WithMinimum(0f)
                .WithMaximum(100f);

            Assert.Equal(100f, resource.Value);

            resource.Decrease(150f);

            Assert.Equal(0f, resource.Value);
        }

        [Fact]
        public void DeclaringBoundsPublishesAnActualValueChange()
        {
            // 验证声明边界导致当前 Value 被限制时由协调器公开变化。
            var resource = new Resource(120f);
            var changes = new System.Collections.Generic.List<(float Previous, float Current)>();
            resource.OnValueChanged += (previous, current) => changes.Add((previous, current));

            resource.WithMinimum(0f).WithMaximum(100f);

            Assert.Equal(new[] { (120f, 100f) }, changes);
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
        public void WithMaximumLimitsValueWithoutALowerBound()
        {
            var resource = new Resource(120f)
                .WithMaximum(100f);

            Assert.Equal(100f, resource.Value);
            resource.Decrease(150f);
            Assert.Equal(-50f, resource.Value);
        }

        [Fact]
        public void MinimumAndMaximumCanBeDeclaredInEitherOrder()
        {
            var minimumFirst = new Resource(120f)
                .WithMinimum(0f)
                .WithMaximum(100f);
            var maximumFirst = new Resource(-20f)
                .WithMaximum(100f)
                .WithMinimum(0f);

            Assert.Equal(100f, minimumFirst.Value);
            Assert.Equal(0f, maximumFirst.Value);
        }

        [Fact]
        public void EndpointsCanBeReplacedAndInvalidReplacementKeepsPreviousBounds()
        {
            var resource = new Resource(80f)
                .WithMinimum(0f)
                .WithMaximum(100f);

            resource.WithMaximum(60f);
            Assert.Equal(60f, resource.Value);

            Assert.Throws<ArgumentOutOfRangeException>(() => resource.WithMinimum(70f));
            resource.Set(-10f);
            Assert.Equal(0f, resource.Value);
        }

        [Fact]
        public void SetRoundsCandidateBeforeApplyingBounds()
        {
            // 验证 Set 先取整候选值，再应用 Resource 固有边界。
            var resource = new Resource(5f, RoundingMode.Floor)
                .WithMinimum(1.5f)
                .WithMaximum(10f);

            resource.Set(1.9f);

            Assert.Equal(1.5f, resource.Value);
        }

        [Fact]
        public void ValueChangeEventReportsOnlyActualChanges()
        {
            // 验证 Resource 只在 Value 实际变化时报告旧值和新值。
            var resource = new Resource(100f);
            var changes = new System.Collections.Generic.List<(float Previous, float Current)>();
            resource.OnValueChanged += (previous, current) => changes.Add((previous, current));

            resource.Set(100f);
            resource.Decrease(25f);

            Assert.Equal(new[] { (100f, 75f) }, changes);
        }

        [Fact]
        public void ChangeEventDefersReentrantChangesUntilCurrentNotificationCompletes()
        {
            // 验证事件回调产生的新修改在当前事件全部监听者完成后才按 FIFO 通知。
            var resource = new Resource(100f);
            var notifications = new System.Collections.Generic.List<string>();
            resource.OnValueChanged += (previous, current) =>
            {
                notifications.Add("first:" + current);
                if (current == 75f) resource.Set(50f);
            };
            resource.OnValueChanged += (previous, current) => notifications.Add("second:" + current);

            resource.Set(75f);

            Assert.Equal(
                new[] { "first:75", "second:75", "first:50", "second:50" },
                notifications);
        }

        [Fact]
        public void ChangeEventContinuesAfterListenerThrows()
        {
            // 验证单个 Resource 监听者异常不会阻断其余监听者。
            var resource = new Resource(100f);
            var notified = false;
            resource.OnValueChanged += (previous, current) => throw new InvalidOperationException("test");
            resource.OnValueChanged += (previous, current) => notified = true;

            resource.Set(75f);

            Assert.True(notified);
        }

        [Fact]
        public void DynamicMaximumClampsValueWhenSourceStatChanges()
        {
            // 验证 Resource 的动态最大边界会随来源 Stat 的 FinalValue 变化而立即钳制 Value。
            var maximum = new Stat(100f);
            var resource = new Resource(80f)
                .WithMinimum(0f)
                .WithMaximum(ValueInput.Final(maximum));

            maximum.BaseValue = 50f;

            Assert.Equal(50f, resource.Value);
        }

        [Fact]
        public void DeclaringDynamicBoundsPublishesAnActualValueChange()
        {
            // 验证声明动态边界导致当前 Value 被限制时公开变化。
            var maximum = new ObservableValue(100f);
            var resource = new Resource(120f);
            var changes = new System.Collections.Generic.List<(float Previous, float Current)>();
            resource.OnValueChanged += (previous, current) => changes.Add((previous, current));

            resource.WithMinimum(0f).WithMaximum(ValueInput.External(maximum));

            Assert.Equal(new[] { (120f, 100f) }, changes);
        }

        [Fact]
        public void DynamicMaximumPreservesRatioWhenConfigured()
        {
            // 验证 PreserveRatio 策略会在动态最大值变化时保持 Resource 填充比例。
            var maximum = new Stat(100f);
            var resource = new Resource(80f)
                .WithMinimum(0f)
                .WithMaximum(ValueInput.Final(maximum))
                .PreserveRatioWhenBoundsChange();

            maximum.BaseValue = 50f;

            Assert.Equal(40f, resource.Value);
        }

        [Fact]
        public void DynamicMaximumTracksCurrentResourceInput()
        {
            // 验证动态边界可以读取另一个 Resource 的当前 Value。
            var capacity = new Resource(100f);
            var resource = new Resource(80f)
                .WithMinimum(0f)
                .WithMaximum(ValueInput.Current(capacity));

            capacity.Set(50f);

            Assert.Equal(50f, resource.Value);
        }

        [Fact]
        public void DynamicMaximumTracksBaseStatInput()
        {
            // 验证动态边界可以读取 Stat 的 BaseValue。
            var maximum = new Stat(100f);
            var resource = new Resource(80f)
                .WithMinimum(0f)
                .WithMaximum(ValueInput.Base(maximum));

            maximum.BaseValue = 50f;

            Assert.Equal(50f, resource.Value);
        }

        [Fact]
        public void SourceEventObservesEntireAffectedGraphAfterRecalculation()
        {
            // 验证来源 Stat 的公开事件派发前，受影响 Resource 已完成同轮更新。
            var maximum = new Stat(100f);
            var observedResourceValue = -1f;
            var resource = new Resource(80f);
            maximum.OnFinalValueChanged += (previous, current) => observedResourceValue = resource.Value;
            resource.WithMinimum(0f).WithMaximum(ValueInput.Final(maximum));

            maximum.BaseValue = 50f;

            Assert.Equal(50f, observedResourceValue);
        }

        [Fact]
        public void DynamicResourceCycleIsRejectedBeforeBoundsChange()
        {
            // 验证会形成 Resource Current 环的动态边界在改变目标前被原子拒绝。
            var first = new Resource(80f);
            var second = new Resource(100f);
            first.WithMinimum(0f).WithMaximum(ValueInput.Current(second));

            Assert.Throws<InvalidOperationException>(() =>
                second.WithMinimum(0f).WithMaximum(ValueInput.Current(first)));

            second.Set(120f);
            Assert.Equal(120f, second.Value);
        }

        [Fact]
        public void ListenerSnapshotIsStableDuringEventDispatch()
        {
            // 验证事件开始时固定监听者快照，派发中的增删从下一事件生效。
            var resource = new Resource(100f);
            var notifications = new System.Collections.Generic.List<string>();
            Action<float, float> second = (previous, current) => notifications.Add("second:" + current);
            Action<float, float> added = (previous, current) => notifications.Add("added:" + current);
            resource.OnValueChanged += (previous, current) =>
            {
                notifications.Add("first:" + current);
                resource.OnValueChanged -= second;
                resource.OnValueChanged += added;
            };
            resource.OnValueChanged += second;

            resource.Set(75f);
            resource.Set(50f);

            Assert.Equal(
                new[] { "first:75", "second:75", "first:50", "added:50" },
                notifications);
        }

        [Fact]
        public void DiagnosticHandlerFailureDoesNotInterruptEventQueue()
        {
            // 验证诊断处理器自身异常不会阻断其他监听者和后续事件。
            Action<Exception> previousHandler = StatsDiagnostics.EventExceptionHandler;
            try
            {
                var resource = new Resource(100f);
                var notified = false;
                StatsDiagnostics.EventExceptionHandler = exception => throw new InvalidOperationException("diagnostic");
                resource.OnValueChanged += (previous, current) => throw new InvalidOperationException("listener");
                resource.OnValueChanged += (previous, current) => notified = true;

                resource.Set(75f);

                Assert.True(notified);
            }
            finally
            {
                StatsDiagnostics.EventExceptionHandler = previousHandler;
            }
        }

        [Fact]
        public void EventFeedbackBeyondBudgetIsReportedAndStopped()
        {
            // 验证动态事件反馈超过内部预算时被报告并停止，而不会无限执行或向外抛出。
            Action<Exception> previousHandler = StatsDiagnostics.EventExceptionHandler;
            try
            {
                var resource = new Resource(0f);
                Exception reported = null;
                StatsDiagnostics.EventExceptionHandler = exception => reported = exception;
                resource.OnValueChanged += (previous, current) => resource.Set(current == 0f ? 1f : 0f);

                resource.Set(1f);

                Assert.NotNull(reported);
                Assert.Contains("feedback budget", reported.Message);
            }
            finally
            {
                StatsDiagnostics.EventExceptionHandler = previousHandler;
            }
        }

        [Fact]
        public void DynamicMinimumAndMaximumBothUpdateResource()
        {
            // 验证 Resource 的动态 Min 和 Max 都会驱动当前 Value 更新。
            var minimum = new ObservableValue(0f);
            var maximum = new ObservableValue(100f);
            var resource = new Resource(50f)
                .WithMinimum(ValueInput.External(minimum))
                .WithMaximum(ValueInput.External(maximum));

            minimum.Value = 60f;

            Assert.Equal(60f, resource.Value);
        }

        [Fact]
        public void InvalidDynamicEndpointsKeepTheLastValidBoundsAndRecoverTogether()
        {
            var minimum = new ObservableValue(0f);
            var maximum = new ObservableValue(100f);
            var resource = new Resource(50f)
                .WithMinimum(ValueInput.External(minimum))
                .WithMaximum(ValueInput.External(maximum));

            Assert.Throws<InvalidOperationException>(() => minimum.Value = 120f);
            Assert.Equal(50f, resource.Value);

            maximum.Value = 150f;
            Assert.Equal(120f, resource.Value);
        }

        [Fact]
        public void ReplacingTheOtherEndpointRecoversAValidDynamicBound()
        {
            var minimum = new ObservableValue(0f);
            var resource = new Resource(-10f)
                .WithMinimum(ValueInput.External(minimum))
                .WithMaximum(100f);

            Assert.Throws<InvalidOperationException>(() => minimum.Value = 120f);

            resource.WithMaximum(150f);

            Assert.Equal(120f, resource.Value);
        }
    }
}
