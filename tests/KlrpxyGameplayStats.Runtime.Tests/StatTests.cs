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
    }
}
