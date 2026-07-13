using Klrpxy.Gameplay.Stats;
using Xunit;

namespace KlrpxyGameplayStats.Runtime.Tests
{
    public sealed class StatTests
    {
        [Fact]
        public void NewStatStartsWithBaseValueAsFinalValue()
        {
            var stat = new Stat(100f);

            Assert.Equal(100f, stat.FinalValue);
        }
    }
}
