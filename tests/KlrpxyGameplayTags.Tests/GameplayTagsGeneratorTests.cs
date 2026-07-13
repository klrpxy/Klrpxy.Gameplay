using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Klrpxy.Gameplay.Tags.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace KlrpxyGameplayTags.Tests
{
    public sealed class GameplayTagsGeneratorTests
    {
        [Fact]
        public void MarkerAttributeComesFromRuntimeInsteadOfGeneratedConsumerSource()
        {
            // 验证生成标记特性来自 Runtime，而不是重复生成到消费者源码中。
            Compilation compilation = RunGenerator(@"
using Klrpxy.Gameplay.Tags;

[GenerateGameplayTags]
public static partial class ProjectTags
{
    private const string TagTable = ""Unit.Enemy"";
}");

            Assert.DoesNotContain(
                compilation.SyntaxTrees,
                tree => string.Equals(
                    tree.FilePath,
                    "GenerateGameplayTagsAttribute.g.cs",
                    StringComparison.Ordinal));
        }

        [Fact]
        public void GeneratedGameplayInterfaceReadsClassLocalTagTableAndUsesRuntimeTagSetAndQuery()
        {
            // 验证生成接口读取类内 TagTable，并使用 Runtime 的 TagSet 与 TagQuery 行为。
            Compilation outputCompilation = RunGenerator(@"
using Klrpxy.Gameplay.Tags;

namespace Consumer
{
    [GenerateGameplayTags]
    public static partial class ProjectTags
    {
        private const string TagTable = @""# Combat tags
Unit.Enemy.Boss

Ability.Attack"";
    }

    public static class ConsumerContract
    {
        public static bool Verify()
        {
            var tags = new TagSet();
            tags.Add(ProjectTags.Unit.Enemy.Boss);
            return TagQuery.Has(ProjectTags.Unit).Matches(tags)
                && TagQuery.HasExact(ProjectTags.Unit.Enemy.Boss).Matches(tags)
                && !TagQuery.HasExact(ProjectTags.Unit).Matches(tags);
        }
    }
}");

            AssertCompiles(outputCompilation);
            Assert.Equal(true, RunConsumerContract(outputCompilation));
        }

        [Fact]
        public void MultipleRootsInDifferentNamespacesShareOneGeneratedTagUniverse()
        {
            // 验证不同命名空间的多个根标签类共享同一个 Tag universe。
            Compilation outputCompilation = RunGenerator(@"
using Klrpxy.Gameplay.Tags;
using Consumer.Combat;

namespace Consumer.Combat
{
    [GenerateGameplayTags]
    public static partial class Tags
    {
        private const string TagTable = ""Unit.Enemy.Boss"";
    }

}

namespace Consumer.Interface
{
    [GenerateGameplayTags]
    public static partial class Tags
    {
        private const string TagTable = ""Hud.Menu"";
    }
}

namespace Consumer
{
    public static class ConsumerContract
    {
        public static bool Verify()
        {
            var tags = new TagSet();
            tags.Add(Tags.Unit.Enemy.Boss);
            tags.Add(global::Consumer.Interface.Tags.Hud.Menu);
            return TagQuery.All(
                TagQuery.Has(Tags.Unit),
                TagQuery.Has(global::Consumer.Interface.Tags.Hud)).Matches(tags);
        }
    }
}");

            AssertCompiles(outputCompilation);
            Assert.Equal(true, RunConsumerContract(outputCompilation));
        }

        [Fact]
        public void GeneratedGameplayInterfacePreservesTagSetMutationSemantics()
        {
            // 验证生成接口保留 TagSet 添加、移除和查询的变更语义。
            Compilation outputCompilation = RunGenerator(@"
using Klrpxy.Gameplay.Tags;

namespace Consumer
{
    [GenerateGameplayTags]
    public static partial class ProjectTags
    {
        private const string TagTable = ""Unit.Enemy.Boss"";
    }

    public static class ConsumerContract
    {
        public static string Verify()
        {
            var tags = new TagSet();
            TagQuery query = TagQuery.Has(ProjectTags.Unit);
            bool nullRejected;
            try
            {
                tags.Add(null);
                nullRejected = false;
            }
            catch (System.ArgumentNullException)
            {
                nullRejected = true;
            }

            return string.Join(""|"", new[]
            {
                tags.Add(ProjectTags.Unit.Enemy.Boss).ToString(),
                tags.Add(ProjectTags.Unit.Enemy.Boss).ToString(),
                tags.Has(ProjectTags.Unit).ToString(),
                tags.HasExact(ProjectTags.Unit).ToString(),
                query.Matches(tags).ToString(),
                tags.Remove(ProjectTags.Unit.Enemy.Boss).ToString(),
                tags.Remove(ProjectTags.Unit.Enemy.Boss).ToString(),
                query.Matches(tags).ToString(),
                nullRejected.ToString()
            });
        }
    }
}");

            AssertCompiles(outputCompilation);
            Assert.Equal("True|False|True|False|True|True|False|False|True", RunConsumerContract(outputCompilation));
        }

        [Fact]
        public void GeneratedGameplayInterfacePublishesTagSetChangesOnlyForActualMutations()
        {
            // 验证 TagSet 只在集合实际变化时发布变化事件。
            Compilation outputCompilation = RunGenerator(@"
using System.Collections.Generic;
using Klrpxy.Gameplay.Tags;

namespace Consumer
{
    [GenerateGameplayTags]
    public static partial class ProjectTags
    {
        private const string TagTable = ""Unit.Enemy.Boss"";
    }

    public static class ConsumerContract
    {
        public static string Verify()
        {
            var tags = new TagSet();
            var changes = new List<string>();
            tags.OnChanged += change => changes.Add(change.Tag.GetPath() + "":"" + change.Kind);

            tags.Add(ProjectTags.Unit.Enemy.Boss);
            tags.Add(ProjectTags.Unit.Enemy.Boss);
            tags.Remove(ProjectTags.Unit.Enemy.Boss);
            tags.Remove(ProjectTags.Unit.Enemy.Boss);

            return string.Join(""|"", changes);
        }
    }
}");

            AssertCompiles(outputCompilation);
            Assert.Equal("Unit.Enemy.Boss:Added|Unit.Enemy.Boss:Removed", RunConsumerContract(outputCompilation));

            INamedTypeSymbol change = outputCompilation.GetTypeByMetadataName("Consumer.TagSetChange");
            Assert.All(change.GetMembers().OfType<IPropertySymbol>(), property => Assert.Null(property.SetMethod));
        }

        [Fact]
        public void GeneratedTagsRetainCanonicalHierarchyAndControlledConstruction()
        {
            // 验证生成标签保持规范层级关系，并禁止外部随意构造。
            Compilation outputCompilation = RunGenerator(@"
using Klrpxy.Gameplay.Tags;

namespace Consumer
{
    [GenerateGameplayTags]
    public static partial class ProjectTags
    {
        private const string TagTable = ""Unit.Enemy.Boss"";
    }

    public static class ConsumerContract
    {
        public static string Verify()
        {
            Tag unit = ProjectTags.Unit;
            Tag enemy = unit.Enemy;
            Tag boss = enemy.Boss;
            return string.Join(""|"", new[]
            {
                unit.GetPath(),
                enemy.GetPath(),
                boss.GetPath(),
                ReferenceEquals(unit, enemy.GetParent()).ToString(),
                ReferenceEquals(enemy, boss.GetParent()).ToString(),
                ReferenceEquals(boss, ProjectTags.Unit.Enemy.Boss).ToString()
            });
        }
    }
}");

            AssertCompiles(outputCompilation);
            Assert.Equal(
                "Unit|Unit.Enemy|Unit.Enemy.Boss|True|True|True",
                RunConsumerContract(outputCompilation));

            INamedTypeSymbol tag = outputCompilation.GetTypeByMetadataName("Consumer.Tag");
            ClassDeclarationSyntax declaration = (ClassDeclarationSyntax)tag.DeclaringSyntaxReferences.Single().GetSyntax();
            Assert.True(tag.IsSealed);
            Assert.DoesNotContain(declaration.Modifiers, modifier => modifier.IsKind(SyntaxKind.PartialKeyword));
            Assert.All(tag.InstanceConstructors, constructor =>
                Assert.Equal(Accessibility.Private, constructor.DeclaredAccessibility));
        }

        [Fact]
        public void ConsumerCannotConstructOrDeriveGeneratedTags()
        {
            // 验证消费者既不能直接构造 Tag，也不能从生成的 Tag 类型派生。
            Compilation outputCompilation = RunGenerator(@"
using Klrpxy.Gameplay.Tags;

namespace Consumer
{
    [GenerateGameplayTags]
    public static partial class ProjectTags
    {
        private const string TagTable = ""Unit"";
    }

    public sealed class ForgedTag : Tag
    {
    }

    public static class InvalidConsumer
    {
        public static Tag Create() => new Tag(""Forged"");
    }
}");

            string[] errorIds = outputCompilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.Id)
                .ToArray();

            Assert.Contains("CS0509", errorIds);
            Assert.Contains("CS1729", errorIds);
        }

        [Fact]
        public void GeneratedGameplayInterfacePreservesQueryCombinators()
        {
            // 验证生成接口完整保留 All、Any 和 None 查询组合行为。
            Compilation outputCompilation = RunGenerator(@"
using Klrpxy.Gameplay.Tags;

namespace Consumer
{
    [GenerateGameplayTags]
    public static partial class ProjectTags
    {
        private const string TagTable = @""Unit.Enemy.Boss
Ability.Attack"";
    }

    public static class ConsumerContract
    {
        public static string Verify()
        {
            var tags = new TagSet();
            tags.Add(ProjectTags.Unit.Enemy.Boss);
            tags.Add(ProjectTags.Ability.Attack);
            return string.Join(""|"", new[]
            {
                TagQuery.All(ProjectTags.Unit, ProjectTags.Ability.Attack).Matches(tags).ToString(),
                TagQuery.Any(TagQuery.HasExact(ProjectTags.Unit), TagQuery.Has(ProjectTags.Unit)).Matches(tags).ToString(),
                TagQuery.None(ProjectTags.Unit, ProjectTags.Ability).Matches(tags).ToString(),
                TagQuery.All().Matches(tags).ToString(),
                TagQuery.Any().Matches(tags).ToString(),
                TagQuery.None().Matches(tags).ToString()
            });
        }
    }
}");

            AssertCompiles(outputCompilation);
            Assert.Equal("True|True|False|True|False|True", RunConsumerContract(outputCompilation));
        }

        [Fact]
        public void TagTableMustBeTheRootsOnlyPrivateConstStringField()
        {
            // 验证根标签类必须且只能使用私有 const string TagTable 声明标签。
            GenerationResult missing = RunGeneratorWithDiagnostics(@"
using Klrpxy.Gameplay.Tags;
[GenerateGameplayTags]
public static partial class ProjectTags { }");
            GenerationResult nonPrivate = RunGeneratorWithDiagnostics(@"
using Klrpxy.Gameplay.Tags;
[GenerateGameplayTags]
public static partial class ProjectTags { public const string TagTable = ""Unit""; }");

            Assert.Equal("KTAG006", Assert.Single(missing.Diagnostics).Id);
            Assert.Equal("KTAG006", Assert.Single(nonPrivate.Diagnostics).Id);
        }

        [Fact]
        public void EveryRootMustDeclareExactlyOnePrivateConstStringTagTable()
        {
            // 验证每个根标签类都必须恰好声明一个私有 const string TagTable。
            GenerationResult result = RunGeneratorWithDiagnostics(@"
using Klrpxy.Gameplay.Tags;
[GenerateGameplayTags]
public static partial class Missing { }
[GenerateGameplayTags]
public static partial class Wrong { internal const string TagTable = ""Unit""; }
[GenerateGameplayTags]
public static partial class Duplicate
{
    private const string TagTable = ""Unit"";
    private const string TagTable = ""Ability"";
}");

            Assert.Equal(
                new[] { "KTAG006", "KTAG006", "KTAG006" },
                result.Diagnostics.Select(diagnostic => diagnostic.Id));
        }

        [Fact]
        public void LegacyExternalTagTableProducesMigrationDiagnostic()
        {
            // 验证旧版外部 Tag Table 会产生明确的迁移诊断。
            GenerationResult result = RunGeneratorWithDiagnostics(
                @"
using Klrpxy.Gameplay.Tags;
[GenerateGameplayTags]
public static partial class ProjectTags { }",
                new TestAdditionalText(
                    "GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit.Enemy.Boss"));

            Diagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("KTAG007", diagnostic.Id);
            Assert.Contains("TagTable", diagnostic.GetMessage());
        }

        [Fact]
        public void InvalidTagTablePathsAreReportedWhileBlankLinesAndCommentsAreIgnored()
        {
            // 验证非法标签路径会被报告，而空行和注释会被忽略。
            GenerationResult result = RunGeneratorWithDiagnostics(@"
using Klrpxy.Gameplay.Tags;
[GenerateGameplayTags]
public static partial class ProjectTags
{
    private const string TagTable = @""
# ignored
Unit.Enemy
Unit.enemy
Unit.Enemy
"";
}");

            Assert.Equal(new[] { "KTAG003", "KTAG004" }, result.Diagnostics.Select(diagnostic => diagnostic.Id));
        }

        [Fact]
        public void InvalidRootsAndDuplicatePathsAcrossRootsAreRejected()
        {
            // 验证非法根标签类和跨根重复标签路径都会被拒绝。
            GenerationResult invalid = RunGeneratorWithDiagnostics(@"
using Klrpxy.Gameplay.Tags;
[GenerateGameplayTags]
internal static partial class ProjectTags { private const string TagTable = ""Unit""; }");
            GenerationResult duplicate = RunGeneratorWithDiagnostics(@"
using Klrpxy.Gameplay.Tags;
[GenerateGameplayTags]
public static partial class First { private const string TagTable = ""Unit.Enemy""; }
[GenerateGameplayTags]
public static partial class Second { private const string TagTable = ""Unit.Enemy""; }");

            Assert.Equal("KTAG001", Assert.Single(invalid.Diagnostics).Id);
            Diagnostic duplicateDiagnostic = Assert.Single(duplicate.Diagnostics);
            Assert.Equal("KTAG004", duplicateDiagnostic.Id);
            Assert.Contains("Unit.Enemy", duplicateDiagnostic.GetMessage());
        }

        [Fact]
        public void PathsImplicitlyDeclaredByAnotherRootAreRejected()
        {
            // 验证已由其他根隐式声明的父路径不能再次显式声明。
            GenerationResult result = RunGeneratorWithDiagnostics(@"
using Klrpxy.Gameplay.Tags;
[GenerateGameplayTags]
public static partial class First { private const string TagTable = ""Unit.Enemy.Boss""; }
[GenerateGameplayTags]
public static partial class Second { private const string TagTable = ""Unit.Enemy""; }");

            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Id == "KTAG004" && diagnostic.GetMessage().Contains("Unit.Enemy"));
        }

        // 编译并加载消费者程序集，然后调用 ConsumerContract.Verify 返回运行结果。
        private static object RunConsumerContract(Compilation compilation)
        {
            AssertCompiles(compilation);

            using (var assemblyStream = new MemoryStream())
            {
                EmitResult emitResult = compilation.Emit(assemblyStream);
                Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

                assemblyStream.Position = 0;
                Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(assemblyStream);
                MethodInfo verify = assembly.GetType("Consumer.ConsumerContract").GetMethod("Verify");

                return verify.Invoke(null, null);
            }
        }

        // 使用给定消费者源码运行 Tags Generator，并返回生成后的编译结果。
        private static Compilation RunGenerator(string source)
        {
            return RunGeneratorWithDiagnostics(source).Compilation;
        }

        // 创建消费者编译、运行 Tags Generator，并同时返回输出编译和生成诊断。
        private static GenerationResult RunGeneratorWithDiagnostics(
            string source,
            params AdditionalText[] additionalFiles)
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
                GetFrameworkReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { new GameplayTagsGenerator() },
                additionalTexts: additionalFiles,
                parseOptions: new CSharpParseOptions(LanguageVersion.CSharp8));

            driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);
            return new GenerationResult(outputCompilation, diagnostics);
        }

        // 检查编译结果中不存在错误，并在失败时输出全部错误诊断。
        private static void AssertCompiles(Compilation compilation)
        {
            Diagnostic[] errors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();

            Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
        }

        // 收集运行消费者编译所需的 .NET 框架与 Tags Runtime 程序集引用。
        private static MetadataReference[] GetFrameworkReferences()
        {
            return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }

        private sealed class GenerationResult
        {
            public GenerationResult(Compilation compilation, ImmutableArray<Diagnostic> diagnostics)
            {
                Compilation = compilation;
                Diagnostics = diagnostics;
            }

            public Compilation Compilation { get; }

            public ImmutableArray<Diagnostic> Diagnostics { get; }
        }

        private sealed class TestAdditionalText : AdditionalText
        {
            private readonly SourceText text;

            public TestAdditionalText(string path, string value)
            {
                Path = path;
                text = SourceText.From(value);
            }

            public override string Path { get; }

            public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default)
            {
                return text;
            }
        }
    }
}
