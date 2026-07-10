using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Klrpxy.Gameplay.Tags.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace KlrpxyGameplayTags.Tests
{
    public sealed class GameplayTagsGeneratorTests
    {
        // 验证生成器能够识别指向正式标记特性的 C# 类型别名。
        [Fact]
        public void GeneratorRecognizesMarkerAttributeAlias()
        {
            const string source = @"
using Marker = Klrpxy.Gameplay.Tags.GenerateGameplayTagsAttribute;

namespace Consumer
{
    [Marker]
    public static partial class ProjectTags
    {
    }

    public static class ConsumerContract
    {
        public static Tag Read() => ProjectTags.Unit;
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit\n"));

            AssertCompiles(outputCompilation);
        }

        // 验证生成的 Tag 与节点类型不会向消费者暴露可用的构造入口。
        [Fact]
        public void GeneratedTagTypesExposeNoConstructionPath()
        {
            const string source = @"
using Klrpxy.Gameplay.Tags;

namespace Consumer
{
    [GenerateGameplayTags]
    public static partial class ProjectTags
    {
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit.Enemy.Boss\n"),
                new InMemoryAdditionalText(
                    "Assets/Unrelated.OtherAnalyzer.additionalfile",
                    "Ignored\n"));

            INamedTypeSymbol tag = outputCompilation.GetTypeByMetadataName("Consumer.Tag");
            IPropertySymbol rootMember = (IPropertySymbol)outputCompilation
                .GetTypeByMetadataName("Consumer.ProjectTags")
                .GetMembers("Unit")
                .Single();
            INamedTypeSymbol rootNode = (INamedTypeSymbol)rootMember.Type;

            Assert.All(tag.InstanceConstructors, constructor =>
                Assert.Equal(Accessibility.Private, constructor.DeclaredAccessibility));
            Assert.Empty(tag.GetMembers("Equals"));
            Assert.Empty(tag.GetMembers("GetHashCode"));
            Assert.Empty(tag.GetMembers("ToString"));
            Assert.Equal(Accessibility.Public, rootNode.DeclaredAccessibility);
            Assert.True(rootNode.IsSealed);
            Assert.True(SymbolEqualityComparer.Default.Equals(tag, rootNode.BaseType));
            Assert.All(rootNode.InstanceConstructors, constructor =>
                Assert.Equal(Accessibility.Private, constructor.DeclaredAccessibility));
        }

        // 验证消费者无法构造 Tag，也无法声明未在 Tag Table 中登记的具体 Tag 子类。
        [Fact]
        public void ConsumerCannotConstructOrSubclassUndeclaredTags()
        {
            const string source = @"
using Klrpxy.Gameplay.Tags;

namespace Consumer
{
    [GenerateGameplayTags]
    public static partial class ProjectTags
    {
    }

    public sealed class UndeclaredTag : Tag
    {
    }

    public static class InvalidConstruction
    {
        public static Tag Create() => new Tag(""Undeclared"", null);
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit\n"));

            string[] errorIds = outputCompilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.Id)
                .OrderBy(id => id)
                .ToArray();

            Assert.Equal(new[] { "CS0122", "CS1729" }, errorIds);
        }

        // 验证生成器不会把其他命名空间中同名的特性误认为正式生成标记。
        [Fact]
        public void GeneratorIgnoresDifferentAttributeWithSameShortName()
        {
            const string source = @"
namespace Other
{
    public sealed class GenerateGameplayTagsAttribute : System.Attribute
    {
    }
}

namespace Consumer
{
    using Other;

    [GenerateGameplayTags]
    public static partial class Impostor
    {
    }

    [Klrpxy.Gameplay.Tags.GenerateGameplayTags]
    public static partial class ProjectTags
    {
    }

    public static class ConsumerContract
    {
        public static Tag Read() => ProjectTags.Unit;
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit\n"));

            AssertCompiles(outputCompilation);
            Assert.NotNull(outputCompilation.GetTypeByMetadataName("Consumer.ProjectTags")
                .GetMembers("Unit")
                .SingleOrDefault());
            Assert.Empty(outputCompilation.GetTypeByMetadataName("Consumer.Impostor").GetMembers("Unit"));
        }

        // 验证生成结果沿用消费者根类型的命名空间与可访问性。
        [Fact]
        public void GeneratedHierarchyUsesConsumerNamespaceAndAccessibility()
        {
            const string source = @"
using Klrpxy.Gameplay.Tags;

namespace Company
{
    namespace Game
    {
        [GenerateGameplayTags]
        internal static partial class GameplayTags
        {
        }

        public static class ConsumerContract
        {
            public static Tag Read() => GameplayTags.Ability;
        }
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Ability.Cast\n"));

            AssertCompiles(outputCompilation);
            INamedTypeSymbol root = outputCompilation.GetTypeByMetadataName("Company.Game.GameplayTags");
            Assert.Equal(Accessibility.Internal, root.DeclaredAccessibility);
        }

        // 验证合法消费者能够访问完整层级、规范实例、路径、父级和对象身份语义。
        [Fact]
        public void ValidConsumerCanUseGeneratedHierarchy()
        {
            const string source = @"
using Klrpxy.Gameplay.Tags;

namespace Consumer
{
    [GenerateGameplayTags]
    public static partial class ProjectTags
    {
    }

    public static class ConsumerContract
    {
        public static string Verify()
        {
            Tag root = ProjectTags.Unit;
            Tag enemy = ProjectTags.Unit.Enemy;
            Tag boss = ProjectTags.Unit.Enemy.Boss;

            return string.Join(""|"", new[]
            {
                root.GetPath(),
                enemy.GetPath(),
                boss.GetPath(),
                ReferenceEquals(root, enemy.GetParent()).ToString(),
                ReferenceEquals(enemy, boss.GetParent()).ToString(),
                (root.GetParent() == null).ToString(),
                ReferenceEquals(boss, ProjectTags.Unit.Enemy.Boss).ToString(),
                (!root.Equals(enemy)).ToString(),
                (root.GetHashCode() == System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(root)).ToString(),
                (boss.GetHashCode() == System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(boss)).ToString()
            });
        }
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit.Enemy.Boss\n"));

            AssertCompiles(outputCompilation);

            using (var assemblyStream = new MemoryStream())
            {
                EmitResult emitResult = outputCompilation.Emit(assemblyStream);
                Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

                assemblyStream.Position = 0;
                Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(assemblyStream);
                MethodInfo verify = assembly.GetType("Consumer.ConsumerContract").GetMethod("Verify");

                Assert.Equal(
                    "Unit|Unit.Enemy|Unit.Enemy.Boss|True|True|True|True|True|True|True",
                    verify.Invoke(null, null));
            }
        }

        private static Compilation RunGenerator(string source, params AdditionalText[] additionalTexts)
        {
            CSharpCompilation compilation = CSharpCompilation.Create(
                "ConsumerAssembly",
                new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp8)) },
                GetFrameworkReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { new GameplayTagsGenerator() },
                additionalTexts: ImmutableArray.Create(additionalTexts),
                parseOptions: new CSharpParseOptions(LanguageVersion.CSharp8));

            driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out _);
            return outputCompilation;
        }

        private static void AssertCompiles(Compilation compilation)
        {
            Diagnostic[] errors = compilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();

            Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
        }

        private static MetadataReference[] GetFrameworkReferences()
        {
            return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }

        private sealed class InMemoryAdditionalText : AdditionalText
        {
            private readonly SourceText text;

            public InMemoryAdditionalText(string path, string text)
            {
                Path = path;
                this.text = SourceText.From(text, Encoding.UTF8);
            }

            public override string Path { get; }

            public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default)
            {
                return text;
            }
        }
    }
}
