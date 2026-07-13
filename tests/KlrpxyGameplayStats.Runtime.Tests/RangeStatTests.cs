using Klrpxy.Gameplay.Stats;
using Xunit;

namespace KlrpxyGameplayStats.Runtime.Tests
{
    public sealed class RangeStatTests
    {
        [Fact]
        public void NewRangeStatStartsWithProvidedBaseAndFinalRange()
        {
            var stat = new RangeStat(10f, 15f);

            Assert.Equal(
                (10f, 15f, 10f, 15f),
                (stat.BaseRange.Min, stat.BaseRange.Max, stat.FinalRange.Min, stat.FinalRange.Max));
        }
    }
}
