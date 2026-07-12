using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Klrpxy.Gameplay.Tags.Generator
{
    [Generator]
    public sealed class GameplayTagsGenerator : ISourceGenerator
    {
        private static readonly DiagnosticDescriptor InvalidMarkerTarget = new DiagnosticDescriptor(
            "KTAG001",
            "Invalid GenerateGameplayTags target",
            "GenerateGameplayTags can only be applied to a public, non-generic, top-level static partial class.",
            "Klrpxy.Gameplay.Tags",
            DiagnosticSeverity.Error,
            true);

        private static readonly DiagnosticDescriptor MultipleMarkerRoots = new DiagnosticDescriptor(
            "KTAG002",
            "Multiple GenerateGameplayTags roots",
            "Exactly one static partial class can be marked with GenerateGameplayTags; found {0}.",
            "Klrpxy.Gameplay.Tags",
            DiagnosticSeverity.Error,
            true);

        private static readonly DiagnosticDescriptor InvalidPath = new DiagnosticDescriptor(
            "KTAG003",
            "Invalid Tag path",
            "Tag path '{0}' contains invalid segment '{1}'; each segment must match [A-Z][A-Za-z0-9]*.",
            "Klrpxy.Gameplay.Tags",
            DiagnosticSeverity.Error,
            true);

        private static readonly DiagnosticDescriptor DuplicateExplicitDeclaration = new DiagnosticDescriptor(
            "KTAG004",
            "Duplicate explicit Tag declaration",
            "Tag path '{0}' is explicitly declared more than once.",
            "Klrpxy.Gameplay.Tags",
            DiagnosticSeverity.Error,
            true);

        private static readonly DiagnosticDescriptor ReservedSegment = new DiagnosticDescriptor(
            "KTAG005",
            "Reserved Tag path segment",
            "Tag path '{0}' uses reserved segment '{1}'.",
            "Klrpxy.Gameplay.Tags",
            DiagnosticSeverity.Error,
            true);

        private static readonly DiagnosticDescriptor InvalidTagTable = new DiagnosticDescriptor(
            "KTAG006",
            "Invalid TagTable declaration",
            "A marked root requires exactly one private const string TagTable field.",
            "Klrpxy.Gameplay.Tags",
            DiagnosticSeverity.Error,
            true);

        private static readonly HashSet<string> ReservedSegments = new HashSet<string>(
            new[] { "Equals", "GetHashCode", "GetType", "ToString", "GetPath", "GetParent" },
            StringComparer.Ordinal);

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new RootReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            context.AddSource("GenerateGameplayTagsAttribute.g.cs", SourceText.From(AttributeSource, Encoding.UTF8));

            RootReceiver receiver = context.SyntaxReceiver as RootReceiver;
            List<ClassDeclarationSyntax> markedRoots = receiver == null
                ? new List<ClassDeclarationSyntax>()
                : receiver.Roots.Where(candidate => FindOfficialMarker(context.Compilation, candidate) != null).ToList();
            if (markedRoots.Count == 0)
            {
                return;
            }

            ClassDeclarationSyntax invalidRoot = markedRoots.FirstOrDefault(candidate => !IsValidRoot(candidate));
            if (invalidRoot != null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidMarkerTarget,
                    FindOfficialMarker(context.Compilation, invalidRoot).GetLocation()));
                return;
            }

            if (markedRoots.Count > 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MultipleMarkerRoots,
                    FindOfficialMarker(context.Compilation, markedRoots[1]).GetLocation(),
                    markedRoots.Count));
                return;
            }

            ClassDeclarationSyntax root = markedRoots[0];
            INamedTypeSymbol rootSymbol = context.Compilation.GetSemanticModel(root.SyntaxTree)
                .GetDeclaredSymbol(root);
            IFieldSymbol[] tagTableFields = rootSymbol.GetMembers("TagTable")
                .OfType<IFieldSymbol>()
                .ToArray();
            if (tagTableFields.Length != 1 || !IsTagTable(tagTableFields[0]))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidTagTable,
                    FindOfficialMarker(context.Compilation, root).GetLocation()));
                return;
            }

            IFieldSymbol tagTable = tagTableFields[0];
            IReadOnlyList<string[]> paths = ParsePaths(
                context,
                tagTable.Locations[0],
                tagTable.ConstantValue as string ?? string.Empty);
            if (paths != null)
            {
                context.AddSource(
                    "GameplayTags.g.cs",
                    SourceText.From(GenerateHierarchy(root, paths), Encoding.UTF8));
            }
        }

        private const string AttributeSource = @"// <auto-generated />
namespace Klrpxy.Gameplay.Tags
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class GenerateGameplayTagsAttribute : global::System.Attribute
    {
    }
}
";

        private static bool IsValidRoot(ClassDeclarationSyntax root)
        {
            return root.Modifiers.Any(SyntaxKind.PublicKeyword)
                && root.Modifiers.Any(SyntaxKind.StaticKeyword)
                && root.Modifiers.Any(SyntaxKind.PartialKeyword)
                && root.TypeParameterList == null
                && !root.Ancestors().OfType<TypeDeclarationSyntax>().Any();
        }

        private static bool IsTagTable(IFieldSymbol field)
        {
            return field.DeclaredAccessibility == Accessibility.Private
                && field.IsConst
                && field.Type.SpecialType == SpecialType.System_String;
        }

        private static AttributeSyntax FindOfficialMarker(
            Compilation compilation,
            ClassDeclarationSyntax root)
        {
            SemanticModel semanticModel = compilation.GetSemanticModel(root.SyntaxTree);
            foreach (AttributeSyntax attribute in root.AttributeLists.SelectMany(list => list.Attributes))
            {
                IMethodSymbol constructor = semanticModel.GetSymbolInfo(attribute).Symbol as IMethodSymbol;
                if (constructor != null)
                {
                    if (IsOfficialMarkerType(constructor.ContainingType.ToDisplayString()))
                    {
                        return attribute;
                    }

                    continue;
                }

                IdentifierNameSyntax identifier = attribute.Name as IdentifierNameSyntax;
                IAliasSymbol alias = identifier == null ? null : semanticModel.GetAliasInfo(identifier);
                if (alias != null)
                {
                    if (IsOfficialMarkerType(alias.Target.ToDisplayString()))
                    {
                        return attribute;
                    }

                    continue;
                }

                bool? syntaxAliasIsOfficial = identifier == null
                    ? (bool?)null
                    : IsOfficialMarkerAlias(root, identifier.Identifier.ValueText);
                if (syntaxAliasIsOfficial.HasValue)
                {
                    if (syntaxAliasIsOfficial.Value)
                    {
                        return attribute;
                    }

                    continue;
                }

                if (IsMarkerName(attribute.Name.ToString()))
                {
                    return attribute;
                }
            }

            return null;
        }

        private static bool? IsOfficialMarkerAlias(ClassDeclarationSyntax root, string aliasName)
        {
            foreach (SyntaxNode scope in root.Ancestors())
            {
                IEnumerable<UsingDirectiveSyntax> usings;
                NamespaceDeclarationSyntax namespaceDeclaration = scope as NamespaceDeclarationSyntax;
                CompilationUnitSyntax compilationUnit = scope as CompilationUnitSyntax;
                if (namespaceDeclaration != null)
                {
                    usings = namespaceDeclaration.Usings;
                }
                else if (compilationUnit != null)
                {
                    usings = compilationUnit.Usings;
                }
                else
                {
                    continue;
                }

                UsingDirectiveSyntax match = usings.FirstOrDefault(usingDirective =>
                    usingDirective.Alias != null
                    && string.Equals(
                        usingDirective.Alias.Name.Identifier.ValueText,
                        aliasName,
                        StringComparison.Ordinal));
                if (match != null)
                {
                    return IsOfficialMarkerType(NormalizeName(match.Name.ToString()));
                }
            }

            return null;
        }

        private static bool IsMarkerName(string name)
        {
            name = NormalizeName(name);
            return string.Equals(name, "GenerateGameplayTags", StringComparison.Ordinal)
                || string.Equals(name, "GenerateGameplayTagsAttribute", StringComparison.Ordinal)
                || string.Equals(name, "Klrpxy.Gameplay.Tags.GenerateGameplayTags", StringComparison.Ordinal)
                || IsOfficialMarkerType(name);
        }

        private static string NormalizeName(string name)
        {
            const string globalPrefix = "global::";
            return name.StartsWith(globalPrefix, StringComparison.Ordinal)
                ? name.Substring(globalPrefix.Length)
                : name;
        }

        private static bool IsOfficialMarkerType(string name)
        {
            return string.Equals(
                name,
                "Klrpxy.Gameplay.Tags.GenerateGameplayTagsAttribute",
                StringComparison.Ordinal);
        }

        private static IReadOnlyList<string[]> ParsePaths(
            GeneratorExecutionContext context,
            Location location,
            string table)
        {
            var paths = new List<string[]>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var explicitlyDeclared = new HashSet<string>(StringComparer.Ordinal);
            bool hasErrors = false;

            foreach (string rawLine in table.Replace("\r\n", "\n").Split('\n'))
            {
                string value = rawLine.Trim();
                if (value.Length == 0 || value.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] segments = value.Split('.');
                string invalidSegment = segments.FirstOrDefault(segment => !IsValidSegment(segment));
                if (invalidSegment != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidPath, location, value, invalidSegment));
                    hasErrors = true;
                    continue;
                }

                string reservedSegment = segments.FirstOrDefault(ReservedSegments.Contains);
                if (reservedSegment != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(ReservedSegment, location, value, reservedSegment));
                    hasErrors = true;
                    continue;
                }

                if (!explicitlyDeclared.Add(value))
                {
                    context.ReportDiagnostic(Diagnostic.Create(DuplicateExplicitDeclaration, location, value));
                    hasErrors = true;
                    continue;
                }

                for (int length = 1; length <= segments.Length; length++)
                {
                    string path = string.Join(".", segments, 0, length);
                    if (seen.Add(path))
                    {
                        paths.Add(segments.Take(length).ToArray());
                    }
                }
            }

            return hasErrors ? null : paths;
        }

        private static bool IsValidSegment(string segment)
        {
            if (segment.Length == 0 || segment[0] < 'A' || segment[0] > 'Z')
            {
                return false;
            }

            for (int index = 1; index < segment.Length; index++)
            {
                char character = segment[index];
                if ((character < 'A' || character > 'Z')
                    && (character < 'a' || character > 'z')
                    && (character < '0' || character > '9'))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GenerateHierarchy(ClassDeclarationSyntax root, IReadOnlyList<string[]> paths)
        {
            string namespaceName = GetNamespace(root);
            var source = new StringBuilder();

            source.AppendLine("// <auto-generated />");
            if (namespaceName.Length > 0)
            {
                source.Append("namespace ").Append(namespaceName).AppendLine();
                source.AppendLine("{");
            }

            string indent = namespaceName.Length > 0 ? "    " : string.Empty;
            source.Append(indent).Append("public static partial class ")
                .Append(root.Identifier.ValueText).AppendLine();
            source.Append(indent).AppendLine("{");
            foreach (string[] path in paths.Where(path => path.Length == 1))
            {
                source.Append(indent).Append("    public static Tag ").Append(path[0])
                    .Append(" => Tag.").Append(GetFieldName(path)).AppendLine(";");
            }

            source.Append(indent).AppendLine("}");
            source.AppendLine();
            AppendTag(source, indent, paths);
            source.AppendLine();
            AppendTagSet(source, indent);
            source.AppendLine();
            AppendTagQuery(source, indent);
            if (namespaceName.Length > 0)
            {
                source.AppendLine("}");
            }

            return source.ToString();
        }

        private static void AppendTag(StringBuilder source, string indent, IReadOnlyList<string[]> paths)
        {
            source.Append(indent).AppendLine("public sealed class Tag");
            source.Append(indent).AppendLine("{");
            source.Append(indent).AppendLine("    private readonly string path;");
            source.Append(indent).AppendLine("    private readonly Tag parent;");
            source.AppendLine();
            source.Append(indent).AppendLine("    private Tag(string path, Tag parent)");
            source.Append(indent).AppendLine("    {");
            source.Append(indent).AppendLine("        this.path = path;");
            source.Append(indent).AppendLine("        this.parent = parent;");
            source.Append(indent).AppendLine("    }");
            source.AppendLine();
            source.Append(indent).AppendLine("    public string GetPath() => path;");
            source.Append(indent).AppendLine("    public Tag GetParent() => parent;");
            source.AppendLine();

            foreach (string[] path in paths)
            {
                string parent = path.Length == 1 ? "null" : GetFieldName(path.Take(path.Length - 1).ToArray());
                source.Append(indent).Append("    internal static readonly Tag ").Append(GetFieldName(path))
                    .Append(" = new Tag(\"").Append(string.Join(".", path)).Append("\", ")
                    .Append(parent).AppendLine(");");
            }

            foreach (string[] path in paths)
            {
                foreach (string[] child in paths.Where(candidate =>
                    candidate.Length == path.Length + 1 && HasPrefix(candidate, path)))
                {
                    source.Append(indent).Append("    public Tag ").Append(child[child.Length - 1])
                        .Append(" => ").Append(GetFieldName(child)).AppendLine(";");
                }
            }

            source.AppendLine();
            source.Append(indent).AppendLine("    internal static bool IsSameOrDescendant(Tag candidate, Tag queried)");
            source.Append(indent).AppendLine("    {");
            source.Append(indent).AppendLine("        for (Tag current = candidate; current != null; current = current.parent)");
            source.Append(indent).AppendLine("        {");
            source.Append(indent).AppendLine("            if (global::System.Object.ReferenceEquals(current, queried))");
            source.Append(indent).AppendLine("            {");
            source.Append(indent).AppendLine("                return true;");
            source.Append(indent).AppendLine("            }");
            source.Append(indent).AppendLine("        }");
            source.AppendLine();
            source.Append(indent).AppendLine("        return false;");
            source.Append(indent).AppendLine("    }");
            source.Append(indent).AppendLine("}");
        }

        private static void AppendTagSet(StringBuilder source, string indent)
        {
            source.Append(indent).AppendLine("public sealed class TagSet");
            source.Append(indent).AppendLine("{");
            source.Append(indent).AppendLine("    private readonly global::Klrpxy.Gameplay.Tags.Runtime.TagSetRuntime<Tag> runtime =");
            source.Append(indent).AppendLine("        new global::Klrpxy.Gameplay.Tags.Runtime.TagSetRuntime<Tag>();");
            source.AppendLine();
            source.Append(indent).AppendLine("    public bool Add(Tag tag) => runtime.Add(tag);");
            source.Append(indent).AppendLine("    public bool Remove(Tag tag) => runtime.Remove(tag);");
            source.Append(indent).AppendLine("    public bool HasExact(Tag tag) => runtime.HasExact(tag);");
            source.Append(indent).AppendLine("    public bool Has(Tag tag) => runtime.Has(tag, Tag.IsSameOrDescendant);");
            source.Append(indent).AppendLine("    internal global::Klrpxy.Gameplay.Tags.Runtime.TagSetRuntime<Tag> Runtime => runtime;");
            source.Append(indent).AppendLine("}");
        }

        private static void AppendTagQuery(StringBuilder source, string indent)
        {
            source.Append(indent).AppendLine("public sealed class TagQuery");
            source.Append(indent).AppendLine("{");
            source.Append(indent).AppendLine("    private readonly global::Klrpxy.Gameplay.Tags.Runtime.TagQueryRuntime<Tag> runtime;");
            source.AppendLine();
            source.Append(indent).AppendLine("    private TagQuery(global::Klrpxy.Gameplay.Tags.Runtime.TagQueryRuntime<Tag> runtime)");
            source.Append(indent).AppendLine("    {");
            source.Append(indent).AppendLine("        this.runtime = runtime;");
            source.Append(indent).AppendLine("    }");
            source.AppendLine();
            source.Append(indent).AppendLine("    public static TagQuery Has(Tag tag) => new TagQuery(");
            source.Append(indent).AppendLine("        new global::Klrpxy.Gameplay.Tags.Runtime.TagQueryRuntime<Tag>(tags => tags.Has(tag, Tag.IsSameOrDescendant)));");
            source.Append(indent).AppendLine("    public static TagQuery HasExact(Tag tag) => new TagQuery(");
            source.Append(indent).AppendLine("        new global::Klrpxy.Gameplay.Tags.Runtime.TagQueryRuntime<Tag>(tags => tags.HasExact(tag)));");
            source.Append(indent).AppendLine("    public static TagQuery All(params TagQuery[] queries) => new TagQuery(");
            source.Append(indent).AppendLine("        global::Klrpxy.Gameplay.Tags.Runtime.TagQueryRuntime<Tag>.All(ToRuntime(queries)));");
            source.Append(indent).AppendLine("    public static TagQuery All(params Tag[] tags) => All(ToQueries(tags));");
            source.Append(indent).AppendLine("    public static TagQuery All() => All(new TagQuery[0]);");
            source.Append(indent).AppendLine("    public static TagQuery Any(params TagQuery[] queries) => new TagQuery(");
            source.Append(indent).AppendLine("        global::Klrpxy.Gameplay.Tags.Runtime.TagQueryRuntime<Tag>.Any(ToRuntime(queries)));");
            source.Append(indent).AppendLine("    public static TagQuery Any(params Tag[] tags) => Any(ToQueries(tags));");
            source.Append(indent).AppendLine("    public static TagQuery Any() => Any(new TagQuery[0]);");
            source.Append(indent).AppendLine("    public static TagQuery None(params TagQuery[] queries) => new TagQuery(");
            source.Append(indent).AppendLine("        global::Klrpxy.Gameplay.Tags.Runtime.TagQueryRuntime<Tag>.None(ToRuntime(queries)));");
            source.Append(indent).AppendLine("    public static TagQuery None(params Tag[] tags) => None(ToQueries(tags));");
            source.Append(indent).AppendLine("    public static TagQuery None() => None(new TagQuery[0]);");
            source.Append(indent).AppendLine("    public bool Matches(TagSet tags) => runtime.Matches(tags.Runtime);");
            source.AppendLine();
            source.Append(indent).AppendLine("    private static TagQuery[] ToQueries(Tag[] tags)");
            source.Append(indent).AppendLine("    {");
            source.Append(indent).AppendLine("        var queries = new TagQuery[tags.Length];");
            source.Append(indent).AppendLine("        for (int index = 0; index < tags.Length; index++)");
            source.Append(indent).AppendLine("        {");
            source.Append(indent).AppendLine("            queries[index] = Has(tags[index]);");
            source.Append(indent).AppendLine("        }");
            source.AppendLine();
            source.Append(indent).AppendLine("        return queries;");
            source.Append(indent).AppendLine("    }");
            source.AppendLine();
            source.Append(indent).AppendLine("    private static global::Klrpxy.Gameplay.Tags.Runtime.TagQueryRuntime<Tag>[] ToRuntime(TagQuery[] queries)");
            source.Append(indent).AppendLine("    {");
            source.Append(indent).AppendLine("        var runtimes = new global::Klrpxy.Gameplay.Tags.Runtime.TagQueryRuntime<Tag>[queries.Length];");
            source.Append(indent).AppendLine("        for (int index = 0; index < queries.Length; index++)");
            source.Append(indent).AppendLine("        {");
            source.Append(indent).AppendLine("            runtimes[index] = queries[index].runtime;");
            source.Append(indent).AppendLine("        }");
            source.AppendLine();
            source.Append(indent).AppendLine("        return runtimes;");
            source.Append(indent).AppendLine("    }");
            source.Append(indent).AppendLine("}");
        }

        private static bool HasPrefix(string[] candidate, string[] prefix)
        {
            for (int index = 0; index < prefix.Length; index++)
            {
                if (!string.Equals(candidate[index], prefix[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetFieldName(string[] path)
        {
            return "__" + string.Join("_", path);
        }

        private static string GetNamespace(SyntaxNode node)
        {
            return string.Join(".", node.Ancestors()
                .OfType<NamespaceDeclarationSyntax>()
                .Reverse()
                .Select(declaration => declaration.Name.ToString()));
        }

        private sealed class RootReceiver : ISyntaxReceiver
        {
            public List<ClassDeclarationSyntax> Roots { get; } = new List<ClassDeclarationSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                ClassDeclarationSyntax declaration = syntaxNode as ClassDeclarationSyntax;
                if (declaration != null && declaration.AttributeLists.Count > 0)
                {
                    Roots.Add(declaration);
                }
            }
        }
    }
}
