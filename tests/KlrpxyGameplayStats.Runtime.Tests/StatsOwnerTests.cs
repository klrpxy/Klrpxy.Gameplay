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
            var statSet = new TestStatSet();

            var owner = new TestOwner(statSet);

            Assert.Same(owner, statSet.Owner);
        }

        [Fact]
        public void StatSetCannotBeBoundToSecondOwner()
        {
            var statSet = new TestStatSet();
            _ = new TestOwner(statSet);

            Assert.Throws<InvalidOperationException>(() => new TestOwner(statSet));
        }

        [Fact]
        public void OwnerRejectsMissingStatSet()
        {
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
