using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Klrpxy.Gameplay.Stats;
using Klrpxy.Gameplay.Stats.Generator;
using Klrpxy.Gameplay.Tags.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Xunit;

namespace KlrpxyGameplayStats.Tests
{
    public sealed class GameplayStatsGeneratorTests
    {
        [Fact]
        public void GeneratedStatKeyGetsDeclaredStatFromOwnersStatSet()
        {
            // 验证生成的 StatKey 能从 Owner 的实际 StatSet 取得声明的 Stat。
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Stats;

namespace Consumer
{
    public sealed partial class HeroStatSet : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
    }

    public sealed class Hero : StatsOwner<HeroStatSet>
    {
        public Hero(HeroStatSet statSet) : base(statSet) { }
    }

    public static class ConsumerContract
    {
        public static bool Verify()
        {
            var statSet = new HeroStatSet();
            var hero = new Hero(statSet);
            Stat health;
            return HeroStatSet.HealthKey.TryGet(hero.StatSet, out health)
                && object.ReferenceEquals(statSet.Health, health);
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void GeneratedStatKeyRejectsIncompatibleStatSet()
        {
            // 验证生成的 StatKey 不会接受类型不兼容的 StatSet。
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Stats;

namespace Consumer
{
    public sealed partial class HeroStatSet : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
    }

    public sealed partial class ItemStatSet : StatSet
    {
        public Stat Price { get; } = new Stat(25f);
    }

    public static class ConsumerContract
    {
        public static bool Verify()
        {
            Stat health;
            return !HeroStatSet.HealthKey.TryGet(new ItemStatSet(), out health)
                && health == null;
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void GeneratedMemberDescriptionsIncludeAllDeclaredValueKinds()
        {
            // 验证生成的成员描述同时包含 Stat、RangeStat 和 Resource。
            Compilation compilation = RunGenerator(@"
using System.Collections.Generic;
using System.Linq;
using Klrpxy.Gameplay.Stats;

namespace Consumer
{
    public sealed partial class HeroStatSet : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
        public RangeStat Damage { get; } = new RangeStat(10f, 15f);
        public Resource Mana { get; } = new Resource(50f);

        public bool HasCompleteGeneratedDescription()
        {
            var members = new List<StatMemberDescriptor>();
            AppendGeneratedMembers(members);
            return members.Count == 3
                && members.Any(member => member.Kind == StatMemberKind.Stat)
                && members.Any(member => member.Kind == StatMemberKind.RangeStat)
                && members.Any(member => member.Kind == StatMemberKind.Resource);
        }
    }

    public static class ConsumerContract
    {
        public static bool Verify() => new HeroStatSet().HasCompleteGeneratedDescription();
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void MultiFilePartialStatSetGeneratesOnce()
        {
            // 验证分散在多个声明中的 partial StatSet 只生成一份代码。
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Stats;

namespace Consumer
{
    public sealed partial class HeroStatSet : StatSet
    {
    }

    public sealed partial class HeroStatSet
    {
        public Stat Health { get; } = new Stat(100f);
    }

    public static class ConsumerContract
    {
        public static bool Verify()
        {
            var statSet = new HeroStatSet();
            Stat health;
            return HeroStatSet.HealthKey.TryGet(statSet, out health);
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void KeywordStatSetAndMemberNamesGenerateValidSource()
        {
            // 验证使用 C# 关键字命名的 StatSet 和成员仍能生成有效源码。
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Stats;

namespace Consumer
{
    public sealed partial class @class : StatSet
    {
        public Stat @event { get; } = new Stat(100f);
    }

    public static class ConsumerContract
    {
        public static bool Verify()
        {
            var statSet = new @class();
            Stat value;
            return @class.eventKey.TryGet(statSet, out value);
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void StatSetsWithCollidingDisplayNamesUseDistinctGeneratedFiles()
        {
            // 验证显示名称碰撞的不同 StatSet 会使用互不冲突的生成文件名。
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Stats;

namespace A
{
    public sealed partial class B_C : StatSet
    {
        public Stat First { get; } = new Stat(1f);
    }
}

namespace A_B
{
    public sealed partial class C : StatSet
    {
        public Stat Second { get; } = new Stat(2f);
    }
}

namespace Consumer
{
    public static class ConsumerContract
    {
        public static bool Verify()
        {
            Stat first;
            Stat second;
            return A.B_C.FirstKey.TryGet(new A.B_C(), out first)
                && A_B.C.SecondKey.TryGet(new A_B.C(), out second);
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        private static Compilation RunGenerator(string source)
        {
            CSharpCompilation compilation = CSharpCompilation.Create(
                "ConsumerAssembly_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        source,
                        new CSharpParseOptions(LanguageVersion.CSharp8),
                        "Consumer.cs")
                },
                GetReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { new GameplayStatsGenerator() },
                parseOptions: new CSharpParseOptions(LanguageVersion.CSharp8));

            driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> generatorDiagnostics);
            Assert.True(
                generatorDiagnostics.Length == 0,
                string.Join(Environment.NewLine, generatorDiagnostics.Select(diagnostic => diagnostic.ToString())));
            return outputCompilation;
        }

        private static object RunConsumerContract(Compilation compilation)
        {
            AssertCompiles(compilation);

            using (var assemblyStream = new MemoryStream())
            {
                EmitResult result = compilation.Emit(assemblyStream);
                Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));

                assemblyStream.Position = 0;
                Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(assemblyStream);
                MethodInfo verify = assembly.GetType("Consumer.ConsumerContract").GetMethod("Verify");
                return verify.Invoke(null, null);
            }
        }

        private static void AssertCompiles(Compilation compilation)
        {
            Diagnostic[] errors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();
            Assert.True(
                errors.Length == 0,
                string.Join(Environment.NewLine, errors.Select(error => error.ToString()))
                    + Environment.NewLine
                    + string.Join(
                        Environment.NewLine,
                        compilation.SyntaxTrees.Skip(1).Select(tree => tree.ToString())));
        }

        private static IEnumerable<MetadataReference> GetReferences()
        {
            var paths = new HashSet<string>(
                ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                    .Split(Path.PathSeparator),
                StringComparer.OrdinalIgnoreCase)
            {
                typeof(StatSet).Assembly.Location,
                typeof(TagSetRuntime<>).Assembly.Location
            };

            return paths.Select(path => MetadataReference.CreateFromFile(path));
        }
    }
}
