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
using Xunit;

namespace KlrpxyGameplayTags.Tests
{
    public sealed class GameplayTagsGeneratorTests
    {
        [Fact]
        public void GeneratedGameplayInterfaceReadsClassLocalTagTableAndUsesRuntimeTagSetAndQuery()
        {
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
        public void InvalidTagTablePathsAreReportedWhileBlankLinesAndCommentsAreIgnored()
        {
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
            GenerationResult result = RunGeneratorWithDiagnostics(@"
using Klrpxy.Gameplay.Tags;
[GenerateGameplayTags]
public static partial class First { private const string TagTable = ""Unit.Enemy.Boss""; }
[GenerateGameplayTags]
public static partial class Second { private const string TagTable = ""Unit.Enemy""; }");

            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Id == "KTAG004" && diagnostic.GetMessage().Contains("Unit.Enemy"));
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

        private static Compilation RunGenerator(string source)
        {
            return RunGeneratorWithDiagnostics(source).Compilation;
        }

        private static GenerationResult RunGeneratorWithDiagnostics(string source)
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
