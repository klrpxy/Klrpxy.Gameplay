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
        public void GeneratedRangeStatKeyGetsDeclaredRangeStat()
        {
            // 验证生成的 RangeStatKey 能从声明的 StatSet 取得区间属性。
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public sealed partial class WeaponStatSet : StatSet
    {
        public RangeStat Damage { get; } = new RangeStat(10f, 20f);
        public Resource Ammo { get; } = new Resource(3f);
    }
    public static class ConsumerContract
    {
        public static bool Verify()
        {
            RangeStat damage;
            return WeaponStatSet.DamageKey.TryGet(new WeaponStatSet(), out damage)
                && damage != null;
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void DerivedStatSetReusesBaseStatKeyWithBuildPath()
        {
            // 验证派生 StatSet 复用基类 Key，且 Key 保留完整构建内路径。
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public abstract partial class CharacterStatSet : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
    }
    public sealed partial class EnemyStatSet : CharacterStatSet { }
    public static class ConsumerContract
    {
        public static bool Verify()
        {
            Stat health;
            return CharacterStatSet.HealthKey.TryGet(new EnemyStatSet(), out health)
                && health != null
                && CharacterStatSet.HealthKey.GetPath().EndsWith(""Consumer.CharacterStatSet.Health"");
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void NonAutoStatPropertyReportsDiagnostic()
        {
            // 验证带自定义 getter 的 Stat 属性会得到可操作的生成诊断。
            GeneratorDriverRunResult result = RunGeneratorWithResult(@"
using Klrpxy.Gameplay.Stats;

namespace Consumer
{
    public sealed partial class HeroStatSet : StatSet
    {
        public Stat Health => new Stat(100f);
    }
}");

            Diagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("KGS001", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        }

        [Fact]
        public void NonPartialStatSetReportsDiagnostic()
        {
            // 验证缺少 partial 的 StatSet 会得到稳定的声明诊断。
            GeneratorDriverRunResult result = RunGeneratorWithResult(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public sealed class HeroStatSet : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
    }
}");

            Assert.Equal("KGS002", Assert.Single(result.Diagnostics).Id);
        }

        [Theory]
        [InlineData("public partial class Outer { public partial class NestedStatSet : StatSet { public Stat Health { get; } = new Stat(1f); } }")]
        [InlineData("public partial class GenericStatSet<T> : StatSet { public Stat Health { get; } = new Stat(1f); }")]
        public void NestedOrGenericStatSetReportsDiagnostic(string declaration)
        {
            // 验证嵌套或泛型 StatSet 会在类型名位置报告声明诊断。
            GeneratorDriverRunResult result = RunGeneratorWithResult("using Klrpxy.Gameplay.Stats; namespace Consumer { " + declaration + " }");

            Diagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("KGS002", diagnostic.Id);
            Assert.Equal("Consumer.cs", diagnostic.Location.SourceTree.FilePath);
        }

        [Fact]
        public void MissingTagsRuntimeReportsInstallationDiagnostic()
        {
            // 验证缺少 Tags Runtime 时会得到明确的安装诊断。
            GeneratorDriverRunResult result = RunGeneratorWithResult(
                "public static class ConsumerContract { }",
                includeTagsRuntime: false);

            Assert.Equal("KGS003", Assert.Single(result.Diagnostics).Id);
        }

        [Fact]
        public void OlderTagsRuntimeReportsInstallationDiagnostic()
        {
            // 验证低于最低兼容版本的 Tags Runtime 会得到安装诊断。
            GeneratorDriverRunResult result = RunGeneratorWithResult(
                "public static class ConsumerContract { }",
                includeTagsRuntime: false,
                tagsRuntimeReference: CreateTagsRuntimeReference("0.1.0.0"));

            Assert.Equal("KGS003", Assert.Single(result.Diagnostics).Id);
        }

        [Fact]
        public void InvalidStatSetDoesNotPreventValidNeighborGeneration()
        {
            // 验证无效 StatSet 失败关闭时，同一编译中的合法邻居仍会生成代码。
            GeneratorDriverRunResult result = RunGeneratorWithResult(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public sealed partial class InvalidStatSet : StatSet
    {
        public Stat Health => new Stat(100f);
    }
    public sealed partial class ValidStatSet : StatSet
    {
        public Stat Attack { get; } = new Stat(20f);
    }
}");

            Assert.Equal("KGS001", Assert.Single(result.Diagnostics).Id);
            Assert.Single(result.Results.Single().GeneratedSources);
        }

        [Theory]
        [InlineData("public Stat Health { get; private set; } = new Stat(100f);")]
        [InlineData("public static Stat Health { get; } = new Stat(100f);")]
        [InlineData("public Stat this[int index] { get { return new Stat(100f); } }")]
        [InlineData("public virtual Stat Health { get; } = new Stat(100f);")]
        public void InvalidStatMemberShapesReportPropertyDiagnostic(string declaration)
        {
            // 验证非法 Stat 属性形状会在其声明位置报告诊断。
            GeneratorDriverRunResult result = RunGeneratorWithResult(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public partial class HeroStatSet : StatSet
    {
        " + declaration + @"
    }
}");

            Diagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("KGS001", diagnostic.Id);
            Assert.Equal("Consumer.cs", diagnostic.Location.SourceTree.FilePath);
        }

        [Fact]
        public void ImplicitlyHiddenStatPropertyReportsDiagnostic()
        {
            // 验证派生 StatSet 不能用 new 隐藏已有 Stat 属性。
            GeneratorDriverRunResult result = RunGeneratorWithResult(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public partial class BaseStatSet : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
    }
    public partial class DerivedStatSet : BaseStatSet
    {
        public Stat Health { get; } = new Stat(50f);
    }
}");

            Diagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("KGS001", diagnostic.Id);
            Assert.Equal("Consumer.cs", diagnostic.Location.SourceTree.FilePath);
        }

        [Fact]
        public void OverriddenStatPropertyReportsDiagnostic()
        {
            // 验证派生 StatSet 不能 override 已声明的 Stat 属性。
            GeneratorDriverRunResult result = RunGeneratorWithResult(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public partial class BaseStatSet : StatSet { public virtual Stat Health { get; } = new Stat(1f); }
    public partial class DerivedStatSet : BaseStatSet { public override Stat Health { get; } = new Stat(2f); }
}");

            Assert.All(result.Diagnostics, diagnostic => Assert.Equal("KGS001", diagnostic.Id));
            Assert.Equal(2, result.Diagnostics.Length);
        }

        [Fact]
        public void ExistingStatKeyMemberReportsConflictDiagnostic()
        {
            // 验证已有同名 Key 成员时，生成器会拒绝生成部分 StatSet。
            GeneratorDriverRunResult result = RunGeneratorWithResult(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public partial class HeroStatSet : StatSet
    {
        public Stat Health { get; } = new Stat(100f);
        public object HealthKey { get; } = null;
    }
}");

            Assert.Equal("KGS004", Assert.Single(result.Diagnostics).Id);
            Assert.Empty(result.Results.Single().GeneratedSources);
        }

        [Fact]
        public void ResourceDoesNotGenerateOrConflictWithKey()
        {
            // 验证 Resource 进入成员描述但不生成 Key，也不与同名成员冲突。
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public partial class HeroStatSet : StatSet
    {
        public Resource Mana { get; } = new Resource(50f);
        public object ManaKey { get; } = null;
    }
    public static class ConsumerContract
    {
        public static bool Verify() => new HeroStatSet().Mana != null;
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

        [Fact]
        public void GeneratedMemberBindingRejectsNullPropertyWithPath()
        {
            // 验证 Runtime 会使用真实生成成员描述拒绝空属性并保留完整路径。
            Compilation compilation = RunGenerator(@"
using System;
using Klrpxy.Gameplay.Stats;
namespace Consumer
{
    public partial class HeroStatSet : StatSet { public Stat Health { get; } }
    public sealed class Hero : StatsOwner<HeroStatSet> { public Hero() : base(new HeroStatSet()) { } }
    public static class ConsumerContract
    {
        public static bool Verify()
        {
            try { new Hero(); return false; }
            catch (InvalidOperationException exception) { return exception.Message.Contains(""HeroStatSet.Health""); }
        }
    }
}");

            Assert.True((bool)RunConsumerContract(compilation));
        }

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

        // 使用给定消费者源码运行 Stats Generator，并返回没有生成错误的输出编译。
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

            driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);
            Assert.True(
                generatorDiagnostics.Length == 0,
                string.Join(Environment.NewLine, generatorDiagnostics.Select(diagnostic => diagnostic.ToString())));
            return outputCompilation;
        }

        // 使用给定消费者源码运行 Stats Generator，并返回公开的生成诊断结果。
        private static GeneratorDriverRunResult RunGeneratorWithResult(string source, bool includeTagsRuntime = true, MetadataReference tagsRuntimeReference = null)
        {
            CSharpCompilation compilation = CSharpCompilation.Create(
                "ConsumerAssembly_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp8), "Consumer.cs") },
                GetReferences(includeTagsRuntime, tagsRuntimeReference),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { new GameplayStatsGenerator() },
                parseOptions: new CSharpParseOptions(LanguageVersion.CSharp8));

            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
            return driver.GetRunResult();
        }

        // 编译并加载消费者程序集，然后调用 ConsumerContract.Verify 返回运行结果。
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

        // 检查编译结果中不存在错误，并在失败时附带生成源码帮助定位问题。
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

        // 收集消费者编译所需的 .NET、Stats Runtime 与 Tags Runtime 程序集引用。
        private static IEnumerable<MetadataReference> GetReferences(bool includeTagsRuntime = true, MetadataReference tagsRuntimeReference = null)
        {
            var paths = new HashSet<string>(
                ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                    .Split(Path.PathSeparator),
                StringComparer.OrdinalIgnoreCase)
            { typeof(StatSet).Assembly.Location };

            if (includeTagsRuntime)
            {
                paths.Add(typeof(TagSetRuntime<>).Assembly.Location);
            }
            else
            {
                paths.Remove(typeof(TagSetRuntime<>).Assembly.Location);
            }

            var references = new List<MetadataReference>(paths.Select(path => MetadataReference.CreateFromFile(path)));
            if (tagsRuntimeReference != null) references.Add(tagsRuntimeReference);
            return references;
        }

        // 创建仅用于消费者编译的指定版本 Tags Runtime 元数据引用。
        private static MetadataReference CreateTagsRuntimeReference(string version)
        {
            CSharpCompilation compilation = CSharpCompilation.Create(
                "KlrpxyGameplayTags.Runtime",
                new[] { CSharpSyntaxTree.ParseText("using System.Reflection; [assembly: AssemblyVersion(\"" + version + "\")]", new CSharpParseOptions(LanguageVersion.CSharp8)) },
                GetReferences(includeTagsRuntime: false),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using (var stream = new MemoryStream())
            {
                Assert.True(compilation.Emit(stream).Success);
                return MetadataReference.CreateFromImage(stream.ToArray());
            }
        }
    }
}
