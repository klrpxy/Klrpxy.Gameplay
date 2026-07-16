using System.Linq;
using Klrpxy.Gameplay.Stats;
using Xunit;

namespace KlrpxyGameplayStats.Runtime.Tests
{
    public sealed class StatSubjectNamingTests
    {
        [Fact]
        public void RuntimeExportsOnlyStatSubjectNames()
        {
            string[] exportedNames = typeof(Stat).Assembly
                .GetExportedTypes()
                .Select(type => type.Name)
                .ToArray();

            Assert.Contains("StatSubject", exportedNames);
            Assert.Contains("StatSubject`1", exportedNames);
            Assert.Contains("StatSubjectGroup", exportedNames);
            Assert.DoesNotContain("StatsOwner", exportedNames);
            Assert.DoesNotContain("StatsOwner`1", exportedNames);
            Assert.DoesNotContain("StatsOwnerGroup", exportedNames);
        }

        [Fact]
        public void RuntimeDoesNotExportLegacyModifierRegistrationApi()
        {
            var runtime = typeof(Stat).Assembly;
            string[] exportedNames = runtime
                .GetExportedTypes()
                .Select(type => type.Name)
                .ToArray();

            Assert.DoesNotContain("Modifier", exportedNames);
            Assert.DoesNotContain("ModifierValue", exportedNames);
            Assert.DoesNotContain(
                typeof(StatSubject).GetMethods(),
                method => method.Name == "AddModifier");
            Assert.DoesNotContain(
                typeof(StatSubjectGroup).GetMethods(),
                method => method.Name == "AddModifier");
        }
    }
}
