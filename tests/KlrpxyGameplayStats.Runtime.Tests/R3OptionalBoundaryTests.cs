using System;
using System.Linq;
using Xunit;

namespace Klrpxy.Gameplay.Stats.Tests
{
    public sealed class R3OptionalBoundaryTests
    {
        [Fact]
        public void CoreRuntimeCompilesLoadsAndRunsWithoutR3Reference()
        {
            var stat = new Stat(10f);

            stat.BaseValue = 15f;

            Assert.Equal(15f, stat.FinalValue);
            Assert.DoesNotContain(
                typeof(Stat).Assembly.GetReferencedAssemblies(),
                reference => string.Equals(reference.Name, "R3", StringComparison.Ordinal));
            Assert.DoesNotContain(
                typeof(Stat).Assembly.GetExportedTypes()
                    .SelectMany(type => type.GetMembers())
                    .Select(member => member.ToString()),
                signature => signature.IndexOf("R3.", StringComparison.Ordinal) >= 0);
        }
    }
}
