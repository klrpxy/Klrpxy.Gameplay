using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Klrpxy.Gameplay.Stats.Generator
{
    [Generator]
    public sealed class GameplayStatsGenerator : ISourceGenerator
    {
        private const string StatSetTypeName = "Klrpxy.Gameplay.Stats.StatSet";
        private const string RangeStatTypeName = "Klrpxy.Gameplay.Stats.RangeStat";
        private const string ResourceTypeName = "Klrpxy.Gameplay.Stats.Resource";
        private const string StatTypeName = "Klrpxy.Gameplay.Stats.Stat";
        private const string TagsRuntimeAssemblyName = "KlrpxyGameplayTags.Runtime";
        private static readonly string[] RequiredTagsRuntimeTypes =
        {
            "Klrpxy.Gameplay.Tags.Runtime.IGameplayTag",
            "Klrpxy.Gameplay.Tags.Runtime.IHierarchicalGameplayTag",
            "Klrpxy.Gameplay.Tags.Runtime.ITagSet",
            "Klrpxy.Gameplay.Tags.Runtime.ITagQuery",
            "Klrpxy.Gameplay.Tags.Runtime.TagSetChange"
        };
        private static readonly DiagnosticDescriptor InvalidStatMember = new DiagnosticDescriptor(
            "KGS001",
            "无效的 StatSet 成员",
            "属性 '{0}' 必须是 public、实例级、get-only 的自动 {1} 属性",
            "GameplayStats",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
        private static readonly DiagnosticDescriptor InvalidStatSet = new DiagnosticDescriptor("KGS002", "无效的 StatSet", "类型 '{0}' 必须是顶层、非泛型的 partial StatSet", "GameplayStats", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor MissingTagsRuntime = new DiagnosticDescriptor("KGS003", "缺少 Gameplay Tags Runtime", "Stats 需要兼容的 KlrpxyGameplayTags.Runtime；请先安装 Gameplay Tags v0.2.1 或更高版本", "GameplayStats", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor GeneratedMemberConflict = new DiagnosticDescriptor("KGS004", "生成成员冲突", "StatSet '{0}' 已声明成员 '{1}'，无法生成同名 StatKey", "GameplayStats", DiagnosticSeverity.Error, true);

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new StatSetReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (!context.Compilation.ReferencedAssemblyNames.Any(reference => string.Equals(reference.Name, "KlrpxyGameplayStats.Runtime", StringComparison.Ordinal)))
            {
                return;
            }

            if (!HasCompatibleTagsRuntime(context.Compilation))
            {
                context.ReportDiagnostic(Diagnostic.Create(MissingTagsRuntime, Location.None));
                return;
            }

            var receiver = (StatSetReceiver)context.SyntaxReceiver;
            var generatedStatSets = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (ClassDeclarationSyntax declaration in receiver.Candidates)
            {
                SemanticModel semanticModel = context.Compilation.GetSemanticModel(declaration.SyntaxTree);
                var symbol = semanticModel.GetDeclaredSymbol(declaration) as INamedTypeSymbol;
                if (!IsStatSet(symbol) || !generatedStatSets.Add(symbol))
                {
                    continue;
                }

                if (!IsSupportedStatSet(declaration, symbol))
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidStatSet, declaration.Identifier.GetLocation(), symbol.Name));
                    continue;
                }

                IPropertySymbol[] declaredProperties = symbol.GetMembers().OfType<IPropertySymbol>().ToArray();
                bool hasInvalidMember = false;
                foreach (IPropertySymbol property in declaredProperties)
                {
                    MemberKind? kind = GetMemberKind(property);
                    if (kind.HasValue && !IsValidMember(property))
                    {
                        hasInvalidMember = true;
                        context.ReportDiagnostic(Diagnostic.Create(
                            InvalidStatMember,
                            property.Locations.FirstOrDefault(),
                            property.Name,
                            kind.Value));
                    }
                }

                if (hasInvalidMember)
                {
                    continue;
                }

                IPropertySymbol[] members = declaredProperties
                    .Where(member => GetMemberKind(member).HasValue)
                    .ToArray();
                if (members.Length == 0)
                {
                    continue;
                }

                IPropertySymbol conflict = members.FirstOrDefault(member => GetMemberKind(member) != MemberKind.Resource && symbol.GetMembers(member.Name + "Key").Length > 0);
                if (conflict != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(GeneratedMemberConflict, conflict.Locations.FirstOrDefault(), symbol.Name, conflict.Name + "Key"));
                    continue;
                }

                context.AddSource(
                    GetHintName(generatedStatSets.Count),
                    SourceText.From(GenerateStatSet(context.Compilation.AssemblyName, symbol, members), Encoding.UTF8));
            }
        }

        private static bool HasCompatibleTagsRuntime(Compilation compilation)
        {
            if (!compilation.ReferencedAssemblyNames.Any(reference =>
                string.Equals(reference.Name, TagsRuntimeAssemblyName, StringComparison.Ordinal)
                && reference.Version >= new Version(0, 2, 0, 0)))
            {
                return false;
            }

            return RequiredTagsRuntimeTypes.All(typeName =>
            {
                INamedTypeSymbol type = compilation.GetTypeByMetadataName(typeName);
                return type != null
                    && string.Equals(type.ContainingAssembly.Name, TagsRuntimeAssemblyName, StringComparison.Ordinal);
            });
        }

        private static bool IsSupportedStatSet(
            ClassDeclarationSyntax declaration,
            INamedTypeSymbol symbol)
        {
            if (symbol == null
                || declaration.TypeParameterList != null
                || !declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword))
                || (!(declaration.Parent is CompilationUnitSyntax)
                    && !(declaration.Parent is NamespaceDeclarationSyntax)))
            {
                return false;
            }

            return IsStatSet(symbol);
        }

        private static bool IsStatSet(INamedTypeSymbol symbol)
        {
            if (symbol == null)
            {
                return false;
            }

            for (INamedTypeSymbol current = symbol.BaseType; current != null; current = current.BaseType)
            {
                if (string.Equals(current.ToDisplayString(), StatSetTypeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static MemberKind? GetMemberKind(IPropertySymbol property)
        {
            string typeName = property.Type.ToDisplayString();
            if (string.Equals(typeName, StatTypeName, StringComparison.Ordinal))
            {
                return MemberKind.Stat;
            }

            if (string.Equals(typeName, RangeStatTypeName, StringComparison.Ordinal))
            {
                return MemberKind.RangeStat;
            }

            return string.Equals(typeName, ResourceTypeName, StringComparison.Ordinal)
                ? MemberKind.Resource
                : (MemberKind?)null;
        }

        private static bool IsValidMember(IPropertySymbol property)
        {
            var declaration = property.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax() as PropertyDeclarationSyntax)
                .FirstOrDefault(syntax => syntax != null);
            return property.DeclaredAccessibility == Accessibility.Public
                && !property.IsStatic
                && !property.IsVirtual
                && !property.IsOverride
                && property.GetMethod != null
                && property.SetMethod == null
                && declaration != null
                && declaration.ExpressionBody == null
                && declaration.AccessorList != null
                && declaration.AccessorList.Accessors.Count == 1
                && declaration.AccessorList.Accessors[0].IsKind(SyntaxKind.GetAccessorDeclaration)
                && declaration.AccessorList.Accessors[0].Body == null
                && declaration.AccessorList.Accessors[0].ExpressionBody == null
                && !declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.NewKeyword))
                && !HidesInheritedStatMember(property);
        }

        private static bool HidesInheritedStatMember(IPropertySymbol property)
        {
            for (INamedTypeSymbol type = property.ContainingType.BaseType; type != null; type = type.BaseType)
            {
                if (type.GetMembers(property.Name).OfType<IPropertySymbol>().Any(member => GetMemberKind(member).HasValue))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GenerateStatSet(
            string assemblyName,
            INamedTypeSymbol statSet,
            IReadOnlyList<IPropertySymbol> members)
        {
            string namespaceName = statSet.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : statSet.ContainingNamespace.ToDisplayString();
            string accessibility = statSet.DeclaredAccessibility == Accessibility.Public
                ? "public"
                : "internal";
            var source = new StringBuilder();
            source.AppendLine("// <auto-generated />");

            if (namespaceName.Length > 0)
            {
                source.Append("namespace ").Append(namespaceName).AppendLine();
                source.AppendLine("{");
            }

            string indent = namespaceName.Length > 0 ? "    " : string.Empty;
            source.Append(indent).Append(accessibility).Append(" partial class ")
                .Append(EscapeIdentifier(statSet.Name)).AppendLine();
            source.Append(indent).AppendLine("{");

            foreach (IPropertySymbol stat in members.Where(member => GetMemberKind(member) == MemberKind.Stat || GetMemberKind(member) == MemberKind.RangeStat))
            {
                string path = assemblyName + "::" + statSet.ToDisplayString() + "." + stat.Name;
                string statType = stat.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                source.Append(indent).Append("    public static readonly global::Klrpxy.Gameplay.Stats.StatKey<")
                    .Append(statType).Append("> ")
                    .Append(stat.Name).AppendLine("Key =");
                source.Append(indent).Append("        CreateKey<").Append(statType).Append(">(typeof(")
                    .Append(statSet.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Append(") , \"")
                    .Append(path).Append("\", set => ((")
                    .Append(statSet.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Append(")set).")
                    .Append(EscapeIdentifier(stat.Name)).AppendLine(");");
            }

            source.AppendLine();
            source.Append(indent).AppendLine("    private static readonly global::Klrpxy.Gameplay.Stats.StatMemberDescriptor[] __GeneratedMembers =");
            source.Append(indent).AppendLine("    {");
            foreach (IPropertySymbol member in members)
            {
                string path = assemblyName + "::" + statSet.ToDisplayString() + "." + member.Name;
                string memberType = member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                source.Append(indent).Append("        CreateMember<").Append(memberType).Append(">(\"")
                    .Append(path).Append("\", global::Klrpxy.Gameplay.Stats.StatMemberKind.")
                    .Append(GetMemberKind(member).Value).Append(", set => ((")
                    .Append(statSet.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).Append(")set).")
                    .Append(EscapeIdentifier(member.Name)).AppendLine("),");
            }

            source.Append(indent).AppendLine("    };");
            source.AppendLine();
            source.Append(indent).AppendLine("    protected override void AppendGeneratedMembers(global::System.Collections.Generic.ICollection<global::Klrpxy.Gameplay.Stats.StatMemberDescriptor> members)");
            source.Append(indent).AppendLine("    {");
            source.Append(indent).AppendLine("        base.AppendGeneratedMembers(members);");
            source.Append(indent).AppendLine("        foreach (global::Klrpxy.Gameplay.Stats.StatMemberDescriptor member in __GeneratedMembers)");
            source.Append(indent).AppendLine("        {");
            source.Append(indent).AppendLine("            members.Add(member);");
            source.Append(indent).AppendLine("        }");
            source.Append(indent).AppendLine("    }");

            source.Append(indent).AppendLine("}");
            if (namespaceName.Length > 0)
            {
                source.AppendLine("}");
            }

            return source.ToString();
        }

        private static string EscapeIdentifier(string identifier)
        {
            return SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None
                ? identifier
                : "@" + identifier;
        }

        private static string GetHintName(int generatedStatSetCount)
        {
            return "KlrpxyGameplayStats." + generatedStatSetCount + ".g.cs";
        }

        private sealed class StatSetReceiver : ISyntaxReceiver
        {
            public List<ClassDeclarationSyntax> Candidates { get; } = new List<ClassDeclarationSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                var declaration = syntaxNode as ClassDeclarationSyntax;
                if (declaration != null)
                {
                    Candidates.Add(declaration);
                }
            }
        }

        private enum MemberKind
        {
            Stat,
            RangeStat,
            Resource
        }
    }
}
