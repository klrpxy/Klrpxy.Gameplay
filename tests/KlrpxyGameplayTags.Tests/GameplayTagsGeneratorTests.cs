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

        // 验证 Tag Table 会忽略空行和以 # 开头的整行注释。
        [Fact]
        public void TagTableIgnoresBlankLinesAndFullLineComments()
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
        public static Tag Read() => ProjectTags.Unit.Enemy;
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "\n# Unit tags\n   # Enemy tags\nUnit.Enemy\n"));

            AssertCompiles(outputCompilation);
        }

        // 验证非法 Tag 路径段通过 KTAG003 精确定位到 AdditionalFile 行。
        [Fact]
        public void InvalidPathSegmentReportsExactAdditionalFileLine()
        {
            const string tablePath = "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile";
            GenerationResult result = RunGeneratorWithDiagnostics(
                @"
using Klrpxy.Gameplay.Tags;

namespace Consumer
{
    [GenerateGameplayTags]
    public static partial class ProjectTags
    {
    }
}",
                new InMemoryAdditionalText(tablePath, "Unit\nUnit.invalid-name\n"));

            Diagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("KTAG003", diagnostic.Id);
            Assert.Equal(
                "Tag path 'Unit.invalid-name' contains invalid segment 'invalid-name'; each segment must match [A-Z][A-Za-z0-9]*.",
                diagnostic.GetMessage());
            Assert.Equal(tablePath, diagnostic.Location.GetLineSpan().Path);
            Assert.Equal(new LinePosition(1, 0), diagnostic.Location.GetLineSpan().StartLinePosition);
            Assert.Equal(new LinePosition(1, 17), diagnostic.Location.GetLineSpan().EndLinePosition);
        }

        // 验证重复显式声明通过 KTAG004 精确定位到第二次声明行。
        [Fact]
        public void DuplicateExplicitDeclarationReportsExactAdditionalFileLine()
        {
            const string tablePath = "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile";
            GenerationResult result = RunGeneratorWithDiagnostics(
                @"
using Klrpxy.Gameplay.Tags;

[GenerateGameplayTags]
public static partial class ProjectTags
{
}",
                new InMemoryAdditionalText(tablePath, "Unit.Enemy\nUnit.Enemy\n"));

            Diagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("KTAG004", diagnostic.Id);
            Assert.Equal("Tag path 'Unit.Enemy' is explicitly declared more than once.", diagnostic.GetMessage());
            Assert.Equal(tablePath, diagnostic.Location.GetLineSpan().Path);
            Assert.Equal(new LinePosition(1, 0), diagnostic.Location.GetLineSpan().StartLinePosition);
            Assert.Equal(new LinePosition(1, 10), diagnostic.Location.GetLineSpan().EndLinePosition);
        }

        // 验证显式声明祖先后再声明后代不会被视为重复声明。
        [Fact]
        public void ExplicitAncestorAndDescendantAreNotDuplicates()
        {
            Compilation outputCompilation = RunGenerator(
                @"
using Klrpxy.Gameplay.Tags;

[GenerateGameplayTags]
public static partial class ProjectTags
{
}

public static class ConsumerContract
{
    public static Tag Read() => ProjectTags.Unit.Enemy;
}",
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit\nUnit.Enemy\n"));

            AssertCompiles(outputCompilation);
        }

        // 验证所有保留成员名都通过 KTAG005 精确定位到各自的 AdditionalFile 行。
        [Fact]
        public void ReservedSegmentsReportExactAdditionalFileLines()
        {
            const string tablePath = "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile";
            GenerationResult result = RunGeneratorWithDiagnostics(
                @"
using Klrpxy.Gameplay.Tags;

[GenerateGameplayTags]
public static partial class ProjectTags
{
}",
                new InMemoryAdditionalText(
                    tablePath,
                    "Unit.Equals\nUnit.GetHashCode\nUnit.GetType\nUnit.ToString\nUnit.GetPath\nUnit.GetParent\n"));

            string[] reservedNames =
            {
                "Equals",
                "GetHashCode",
                "GetType",
                "ToString",
                "GetPath",
                "GetParent"
            };
            Assert.Equal(reservedNames.Length, result.Diagnostics.Length);
            for (int index = 0; index < reservedNames.Length; index++)
            {
                Diagnostic diagnostic = result.Diagnostics[index];
                Assert.Equal("KTAG005", diagnostic.Id);
                Assert.Equal(
                    $"Tag path 'Unit.{reservedNames[index]}' uses reserved segment '{reservedNames[index]}'.",
                    diagnostic.GetMessage());
                Assert.Equal(tablePath, diagnostic.Location.GetLineSpan().Path);
                Assert.Equal(index, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
                Assert.Equal(0, diagnostic.Location.GetLineSpan().StartLinePosition.Character);
            }
        }

        // 验证没有生成标记的编译不会生成 Tag 层级，也不会校验 Tag Table。
        [Fact]
        public void CompilationWithoutMarkerGeneratesNoHierarchyOrDiagnostics()
        {
            GenerationResult result = RunGeneratorWithDiagnostics(
                "namespace Consumer { public static class Unmarked { } }",
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "invalid\n"));

            Assert.Empty(result.Diagnostics);
            Assert.Null(result.Compilation.GetTypeByMetadataName("Consumer.Tag"));
        }

        // 验证非法生成标记目标通过 KTAG001 精确定位到标记属性。
        [Fact]
        public void InvalidMarkerTargetReportsMarkerLocation()
        {
            GenerationResult result = RunGeneratorWithDiagnostics(
                "using Klrpxy.Gameplay.Tags;\n\nnamespace Consumer\n{\n    [GenerateGameplayTags]\n    public class ProjectTags\n    {\n    }\n}",
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit\n"));

            Diagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("KTAG001", diagnostic.Id);
            Assert.Equal(
                "GenerateGameplayTags can only be applied to a non-generic, top-level static partial class.",
                diagnostic.GetMessage());
            Assert.Equal("Consumer.cs", diagnostic.Location.GetLineSpan().Path);
            Assert.Equal(new LinePosition(4, 5), diagnostic.Location.GetLineSpan().StartLinePosition);
            Assert.Equal(new LinePosition(4, 25), diagnostic.Location.GetLineSpan().EndLinePosition);
        }

        // 验证嵌套或泛型根通过 KTAG001 拒绝，而不会扩展错误的顶层类型。
        [Fact]
        public void NestedAndGenericMarkerTargetsReportInvalidTarget()
        {
            GenerationResult nestedResult = RunGeneratorWithDiagnostics(
                "using Klrpxy.Gameplay.Tags;\n\npublic static class Container\n{\n    [GenerateGameplayTags]\n    public static partial class ProjectTags { }\n}\n",
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit\n"));
            GenerationResult genericResult = RunGeneratorWithDiagnostics(
                "using Klrpxy.Gameplay.Tags;\n\n[GenerateGameplayTags]\npublic static partial class ProjectTags<T> { }\n",
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit\n"));

            Assert.Equal("KTAG001", Assert.Single(nestedResult.Diagnostics).Id);
            Assert.Equal("KTAG001", Assert.Single(genericResult.Diagnostics).Id);
        }

        // 验证多个合法生成根通过 KTAG002 精确定位到第二个标记属性。
        [Fact]
        public void MultipleMarkerRootsReportSecondMarkerLocation()
        {
            GenerationResult result = RunGeneratorWithDiagnostics(
                "using Klrpxy.Gameplay.Tags;\n\n[GenerateGameplayTags]\npublic static partial class First { }\n\n[GenerateGameplayTags]\npublic static partial class Second { }\n",
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit\n"));

            Diagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("KTAG002", diagnostic.Id);
            Assert.Equal(
                "Exactly one static partial class can be marked with GenerateGameplayTags; found 2.",
                diagnostic.GetMessage());
            Assert.Equal("Consumer.cs", diagnostic.Location.GetLineSpan().Path);
            Assert.Equal(new LinePosition(5, 1), diagnostic.Location.GetLineSpan().StartLinePosition);
            Assert.Equal(new LinePosition(5, 21), diagnostic.Location.GetLineSpan().EndLinePosition);
        }

        // 验证缺失 Tag Table 通过 KTAG006 精确定位到生成标记。
        [Fact]
        public void MissingTagTableReportsMarkerLocation()
        {
            GenerationResult result = RunGeneratorWithDiagnostics(
                "using Klrpxy.Gameplay.Tags;\n\n[GenerateGameplayTags]\npublic static partial class ProjectTags { }\n");

            Diagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("KTAG006", diagnostic.Id);
            Assert.Equal(
                "A marked compilation requires GameplayTags.KlrpxyGameplayTags.additionalfile, but no matching Tag Table was provided.",
                diagnostic.GetMessage());
            Assert.Equal("Consumer.cs", diagnostic.Location.GetLineSpan().Path);
            Assert.Equal(new LinePosition(2, 1), diagnostic.Location.GetLineSpan().StartLinePosition);
            Assert.Equal(new LinePosition(2, 21), diagnostic.Location.GetLineSpan().EndLinePosition);
        }

        // 验证歧义 Tag Table 通过 KTAG007 精确定位到第二个冲突文件。
        [Fact]
        public void AmbiguousTagTablesReportSecondFileLocation()
        {
            const string secondTablePath =
                "Packages/Feature/GameplayTags.KlrpxyGameplayTags.additionalfile";
            GenerationResult result = RunGeneratorWithDiagnostics(
                "using Klrpxy.Gameplay.Tags;\n\n[GenerateGameplayTags]\npublic static partial class ProjectTags { }\n",
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit\n"),
                new InMemoryAdditionalText(secondTablePath, "Ability\n"));

            Diagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("KTAG007", diagnostic.Id);
            Assert.Equal(
                "A marked compilation requires exactly one GameplayTags.KlrpxyGameplayTags.additionalfile, but 2 matching Tag Tables were provided.",
                diagnostic.GetMessage());
            Assert.Equal(secondTablePath, diagnostic.Location.GetLineSpan().Path);
            Assert.Equal(new LinePosition(0, 0), diagnostic.Location.GetLineSpan().StartLinePosition);
            Assert.Equal(new LinePosition(0, 7), diagnostic.Location.GetLineSpan().EndLinePosition);
        }

        // 验证原型确立的 KTAG003、KTAG004、KTAG005 含义和精确行序列保持不变。
        [Fact]
        public void InvalidTablePreservesPrototypeDiagnosticIdsAndLines()
        {
            GenerationResult result = RunGeneratorWithDiagnostics(
                "using Klrpxy.Gameplay.Tags;\n\n[GenerateGameplayTags]\npublic static partial class ProjectTags { }\n",
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "# Invalid examples\nunit\nUnit.enemy\nUnit.Enemy\nUnit.Enemy\nUnit.GetPath\nUnit.Enemy-Boss\n"));

            Assert.Equal(
                new[] { "KTAG003", "KTAG003", "KTAG004", "KTAG005", "KTAG003" },
                result.Diagnostics.Select(diagnostic => diagnostic.Id));
            Assert.Equal(
                new[] { 1, 2, 4, 5, 6 },
                result.Diagnostics.Select(diagnostic =>
                    diagnostic.Location.GetLineSpan().StartLinePosition.Line));
        }

        // 验证行内 # 仍属于路径内容，并通过 KTAG003 报告而不是被当作注释。
        [Fact]
        public void InlineCommentSyntaxIsRejectedAsInvalidPath()
        {
            GenerationResult result = RunGeneratorWithDiagnostics(
                "using Klrpxy.Gameplay.Tags;\n\n[GenerateGameplayTags]\npublic static partial class ProjectTags { }\n",
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit.Enemy # comment\n"));

            Diagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("KTAG003", diagnostic.Id);
            Assert.Equal(0, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
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

        // 验证消费者可以把真实生成的规范 Tag 加入 TagSet 并精确测试成员身份。
        [Fact]
        public void TagSetAddsAndTestsExactGeneratedTagMembership()
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
        public static bool Verify()
        {
            var tags = new TagSet();
            tags.Add(ProjectTags.Unit.Enemy);

            return tags.HasExact(ProjectTags.Unit.Enemy);
        }
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit.Enemy\n"));

            Assert.True((bool)RunConsumerContract(outputCompilation));
        }

        // 验证重复添加同一规范 Tag 不会产生第二份显式状态。
        [Fact]
        public void TagSetRejectsDuplicateCanonicalTag()
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
        public static bool Verify()
        {
            var tags = new TagSet();

            return tags.Add(ProjectTags.Unit.Enemy)
                && !tags.Add(ProjectTags.Unit.Enemy);
        }
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit.Enemy\n"));

            Assert.True((bool)RunConsumerContract(outputCompilation));
        }

        // 验证移除真实生成的规范 Tag 后精确成员身份随之消失。
        [Fact]
        public void TagSetRemovesExactGeneratedTagMembership()
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
        public static bool Verify()
        {
            var tags = new TagSet();
            tags.Add(ProjectTags.Unit.Enemy);

            return tags.Remove(ProjectTags.Unit.Enemy)
                && !tags.HasExact(ProjectTags.Unit.Enemy);
        }
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit.Enemy\n"));

            Assert.True((bool)RunConsumerContract(outputCompilation));
        }

        // 验证 Tag Match 接受同一 Tag 和后代候选，并拒绝祖先候选的反向匹配。
        [Fact]
        public void TagSetMatchesCandidatesDirectionally()
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
        public static bool Verify()
        {
            var descendantTags = new TagSet();
            descendantTags.Add(ProjectTags.Unit.Enemy.Boss);
            var ancestorTags = new TagSet();
            ancestorTags.Add(ProjectTags.Unit);

            return descendantTags.Has(ProjectTags.Unit.Enemy.Boss)
                && descendantTags.Has(ProjectTags.Unit.Enemy)
                && !ancestorTags.Has(ProjectTags.Unit.Enemy);
        }
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit.Enemy.Boss\n"));

            Assert.True((bool)RunConsumerContract(outputCompilation));
        }

        // 验证显式后代匹配祖先时不会把该祖先物化为精确成员。
        [Fact]
        public void TagSetDoesNotMaterializeMatchedAncestors()
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
        public static bool Verify()
        {
            var tags = new TagSet();
            tags.Add(ProjectTags.Unit.Enemy.Boss);

            return tags.Has(ProjectTags.Unit)
                && !tags.HasExact(ProjectTags.Unit)
                && tags.HasExact(ProjectTags.Unit.Enemy.Boss);
        }
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit.Enemy.Boss\n"));

            Assert.True((bool)RunConsumerContract(outputCompilation));
        }

        // 验证 TagQuery.Has 使用 TagSet 的方向性 Tag Match。
        [Fact]
        public void TagQueryHasMatchesGeneratedDescendantTag()
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
        public static bool Verify()
        {
            var tags = new TagSet();
            tags.Add(ProjectTags.Unit.Enemy.Boss);

            return TagQuery.Has(ProjectTags.Unit.Enemy).Matches(tags);
        }
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit.Enemy.Boss\n"));

            Assert.True((bool)RunConsumerContract(outputCompilation));
        }

        // 验证 TagQuery.HasExact 只匹配 TagSet 中的精确成员。
        [Fact]
        public void TagQueryHasExactDoesNotMatchGeneratedDescendantTag()
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
        public static bool Verify()
        {
            var tags = new TagSet();
            tags.Add(ProjectTags.Unit.Enemy.Boss);

            return !TagQuery.HasExact(ProjectTags.Unit.Enemy).Matches(tags)
                && TagQuery.HasExact(ProjectTags.Unit.Enemy.Boss).Matches(tags);
        }
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit.Enemy.Boss\n"));

            Assert.True((bool)RunConsumerContract(outputCompilation));
        }

        // 验证 TagQuery.All 组合嵌套查询时要求每个条件都成立。
        [Fact]
        public void TagQueryAllCombinesNestedQueries()
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
        public static bool Verify()
        {
            var tags = new TagSet();
            tags.Add(ProjectTags.Unit.Enemy.Boss);
            tags.Add(ProjectTags.Ability.Cast);

            return TagQuery.All(
                TagQuery.Has(ProjectTags.Unit.Enemy),
                TagQuery.HasExact(ProjectTags.Ability.Cast)).Matches(tags);
        }
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit.Enemy.Boss\nAbility.Cast\n"));

            Assert.True((bool)RunConsumerContract(outputCompilation));
        }

        // 验证 TagQuery.Any 组合嵌套查询时允许任一条件成立。
        [Fact]
        public void TagQueryAnyCombinesNestedQueries()
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
        public static bool Verify()
        {
            var tags = new TagSet();
            tags.Add(ProjectTags.Unit.Enemy.Boss);

            return TagQuery.Any(
                TagQuery.HasExact(ProjectTags.Ability.Cast),
                TagQuery.Has(ProjectTags.Unit.Enemy)).Matches(tags);
        }
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit.Enemy.Boss\nAbility.Cast\n"));

            Assert.True((bool)RunConsumerContract(outputCompilation));
        }

        // 验证 TagQuery.None 在所有嵌套条件均不成立时匹配。
        [Fact]
        public void TagQueryNoneExcludesNestedQueries()
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
        public static bool Verify()
        {
            var tags = new TagSet();
            tags.Add(ProjectTags.Unit.Player);

            return TagQuery.None(
                TagQuery.Has(ProjectTags.Unit.Enemy),
                TagQuery.HasExact(ProjectTags.Ability.Cast)).Matches(tags);
        }
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit.Player\nUnit.Enemy\nAbility.Cast\n"));

            Assert.True((bool)RunConsumerContract(outputCompilation));
        }

        // 验证 TagQuery 组合器可以在嵌套查询树中求值。
        [Fact]
        public void TagQueryCombinatorsEvaluateNestedQueryTree()
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
        public static bool Verify()
        {
            var tags = new TagSet();
            tags.Add(ProjectTags.Unit.Enemy.Boss);

            return TagQuery.All(
                TagQuery.Any(
                    TagQuery.Has(ProjectTags.Unit.Player),
                    TagQuery.Has(ProjectTags.Unit.Enemy)),
                TagQuery.None(TagQuery.Has(ProjectTags.Ability.Cast))).Matches(tags);
        }
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit.Enemy.Boss\nUnit.Player\nAbility.Cast\n"));

            Assert.True((bool)RunConsumerContract(outputCompilation));
        }

        // 验证 TagQuery.None 在任一嵌套条件成立时拒绝 TagSet。
        [Fact]
        public void TagQueryNoneRejectsMatchingNestedQuery()
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
        public static bool Verify()
        {
            var tags = new TagSet();
            tags.Add(ProjectTags.Unit.Enemy.Boss);

            return !TagQuery.None(TagQuery.Has(ProjectTags.Unit.Enemy)).Matches(tags);
        }
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit.Enemy.Boss\n"));

            Assert.True((bool)RunConsumerContract(outputCompilation));
        }

        // 验证 TagQuery 组合器提供仅接收生成 Tag 的便利重载。
        [Fact]
        public void TagQueryCombinatorsAcceptGeneratedTags()
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
        public static bool Verify()
        {
            var tags = new TagSet();
            tags.Add(ProjectTags.Unit.Enemy.Boss);
            tags.Add(ProjectTags.Ability.Cast);

            return TagQuery.All(ProjectTags.Unit.Enemy, ProjectTags.Ability.Cast).Matches(tags)
                && TagQuery.Any(ProjectTags.Unit.Player, ProjectTags.Ability.Cast).Matches(tags)
                && TagQuery.None(ProjectTags.Unit.Player, ProjectTags.Ability.Jump).Matches(tags);
        }
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit.Enemy.Boss\nUnit.Player\nAbility.Cast\nAbility.Jump\n"));

            Assert.True((bool)RunConsumerContract(outputCompilation));
        }

        // 验证空 TagQuery 组合器遵循定义的确定性真值语义。
        [Fact]
        public void EmptyTagQueryCombinatorsHaveDefinedTruthValues()
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
        public static bool Verify()
        {
            var tags = new TagSet();

            return TagQuery.All().Matches(tags)
                && !TagQuery.Any().Matches(tags)
                && TagQuery.None().Matches(tags);
        }
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit\n"));

            Assert.True((bool)RunConsumerContract(outputCompilation));
        }

        // 验证已创建的 TagQuery 可重复求值且不会改变 TagSet 或查询条件。
        [Fact]
        public void TagQueryIsReusableWithoutMutatingItsInputs()
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
        public static bool Verify()
        {
            var tags = new TagSet();
            tags.Add(ProjectTags.Unit.Enemy.Boss);
            TagQuery[] conditions = { TagQuery.Has(ProjectTags.Unit.Enemy) };
            TagQuery query = TagQuery.All(conditions);
            conditions[0] = TagQuery.Has(ProjectTags.Unit.Player);

            return query.Matches(tags)
                && query.Matches(tags)
                && tags.HasExact(ProjectTags.Unit.Enemy.Boss)
                && !tags.HasExact(ProjectTags.Unit.Player);
        }
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit.Enemy.Boss\nUnit.Player\n"));

            Assert.True((bool)RunConsumerContract(outputCompilation));
        }

        // 验证 TagSet 拒绝不代表规范 Tag 的空引用。
        [Fact]
        public void TagSetRejectsNullTag()
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
        public static bool Verify()
        {
            var tags = new TagSet();

            try
            {
                tags.Add(null);
                return false;
            }
            catch (System.ArgumentNullException)
            {
                return true;
            }
        }
    }
}";

            Compilation outputCompilation = RunGenerator(
                source,
                new InMemoryAdditionalText(
                    "Assets/GameplayTags.KlrpxyGameplayTags.additionalfile",
                    "Unit\n"));

            Assert.True((bool)RunConsumerContract(outputCompilation));
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

            Assert.Equal(
                "Unit|Unit.Enemy|Unit.Enemy.Boss|True|True|True|True|True|True|True",
                RunConsumerContract(outputCompilation));
        }

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

        private static Compilation RunGenerator(string source, params AdditionalText[] additionalTexts)
        {
            return RunGeneratorWithDiagnostics(source, additionalTexts).Compilation;
        }

        private static GenerationResult RunGeneratorWithDiagnostics(
            string source,
            params AdditionalText[] additionalTexts)
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
                additionalTexts: ImmutableArray.Create(additionalTexts),
                parseOptions: new CSharpParseOptions(LanguageVersion.CSharp8));

            driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> diagnostics);
            return new GenerationResult(outputCompilation, diagnostics);
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
    }
}
