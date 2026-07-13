using System;
using Klrpxy.Gameplay.Stats;
using Xunit;

namespace KlrpxyGameplayStats.Runtime.Tests
{
    public sealed class StatsOwnerTests
    {
        [Fact]
        public void OwnerBindsProvidedStatSet()
        {
            // 验证创建 StatsOwner 时会把传入的 StatSet 绑定到该 Owner。
            var statSet = new TestStatSet();

            var owner = new TestOwner(statSet);

            Assert.Same(owner, statSet.Owner);
        }

        [Fact]
        public void StatSetCannotBeBoundToSecondOwner()
        {
            // 验证已绑定的 StatSet 不能再次交给另一个 StatsOwner。
            var statSet = new TestStatSet();
            _ = new TestOwner(statSet);

            Assert.Throws<InvalidOperationException>(() => new TestOwner(statSet));
        }

        [Fact]
        public void OwnerRejectsMissingStatSet()
        {
            // 验证创建 StatsOwner 时必须提供非空 StatSet。
            Assert.Throws<ArgumentNullException>(() => new TestOwner(null));
        }

        private sealed class TestStatSet : StatSet
        {
        }

        private sealed class TestOwner : StatsOwner<TestStatSet>
        {
            public TestOwner(TestStatSet statSet)
                : base(statSet)
            {
            }
        }
    }
}
