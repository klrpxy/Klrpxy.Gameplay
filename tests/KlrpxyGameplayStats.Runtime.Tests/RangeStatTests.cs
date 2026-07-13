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
    }
}
