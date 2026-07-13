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
    }
}
