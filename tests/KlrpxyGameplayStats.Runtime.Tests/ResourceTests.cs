using Klrpxy.Gameplay.Stats;
using Xunit;

namespace KlrpxyGameplayStats.Runtime.Tests
{
    public sealed class ResourceTests
    {
        [Fact]
        public void NewResourceStartsWithProvidedValue()
        {
            var resource = new Resource(100f);

            Assert.Equal(100f, resource.Value);
        }
    }
}
