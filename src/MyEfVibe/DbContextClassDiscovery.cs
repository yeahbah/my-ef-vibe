using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyEfVibe;

internal static partial class DbContextClassDiscovery
{
    [GeneratedRegex(
        @"class\s+(?<Name>\w+)\s*:\s*(?:\w+(?:<[^>]+>)?\s*,\s*)*DbContext\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex DbContextClassDeclarationRegex();

    internal static IReadOnlyList<string> DiscoverDbContextTypeNames(IEnumerable<string> projectDirectories)
        => DiscoverDbContextTypeAliases(projectDirectories)
            .Keys
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

    internal static IReadOnlyDictionary<string, IReadOnlySet<string>> DiscoverDbContextTypeAliases(
        IEnumerable<string> projectDirectories)
    {
        var aliasesByName = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var projectDirectory in projectDirectories)
        {
            if (!Directory.Exists(projectDirectory))
                continue;

            foreach (var sourcePath in LinqProjectSourceWalker.EnumerateSourceFiles(projectDirectory))
            {
                try
                {
                    var text = File.ReadAllText(sourcePath);

                    AddRegexDiscoveredContextNames(text, aliasesByName);
                    AddSyntaxDiscoveredContextAliases(text, aliasesByName);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        return aliasesByName.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlySet<string>)pair.Value,
            StringComparer.Ordinal);
    }

    private static void AddRegexDiscoveredContextNames(
        string text,
        Dictionary<string, HashSet<string>> aliasesByName)
    {
        foreach (Match match in DbContextClassDeclarationRegex().Matches(text))
        {
            if (!match.Groups["Name"].Success)
                continue;

            var name = match.Groups["Name"].Value;
            EnsureAliasSet(aliasesByName, name).Add(name);
        }
    }

    private static void AddSyntaxDiscoveredContextAliases(
        string text,
        Dictionary<string, HashSet<string>> aliasesByName)
    {
        var root = CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot();

        foreach (var typeDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (typeDeclaration is not ClassDeclarationSyntax
                || typeDeclaration.BaseList is null)
                continue;

            var typeName = typeDeclaration.Identifier.Text;
            var baseTypeNames = typeDeclaration.BaseList.Types
                .Select(static baseType => TryGetSimpleTypeName(baseType.Type, out var baseName)
                    ? baseName
                    : null)
                .OfType<string>()
                .ToArray();

            if (!baseTypeNames.Contains("DbContext", StringComparer.Ordinal)
                && !aliasesByName.ContainsKey(typeName))
                continue;

            var aliases = EnsureAliasSet(aliasesByName, typeName);
            aliases.Add(typeName);

            foreach (var baseTypeName in baseTypeNames)
            {
                if (IsDbContextInterfaceAlias(baseTypeName, typeName))
                    aliases.Add(baseTypeName);
            }
        }
    }

    private static HashSet<string> EnsureAliasSet(
        Dictionary<string, HashSet<string>> aliasesByName,
        string typeName)
    {
        if (!aliasesByName.TryGetValue(typeName, out var aliases))
        {
            aliases = new HashSet<string>(StringComparer.Ordinal);
            aliasesByName[typeName] = aliases;
        }

        return aliases;
    }

    private static bool IsDbContextInterfaceAlias(string typeName, string contextTypeName) =>
        typeName.StartsWith('I')
        && typeName.EndsWith("DbContext", StringComparison.Ordinal)
        && !string.Equals(typeName, "IDbContext", StringComparison.Ordinal)
        && !string.Equals(typeName, contextTypeName, StringComparison.Ordinal);

    private static bool TryGetSimpleTypeName(TypeSyntax typeSyntax, out string typeName)
    {
        typeName = string.Empty;

        switch (typeSyntax)
        {
            case IdentifierNameSyntax identifier:
                typeName = identifier.Identifier.Text;
                return true;

            case GenericNameSyntax generic:
                typeName = generic.Identifier.Text;
                return true;

            case QualifiedNameSyntax qualified:
                return TryGetSimpleTypeName(qualified.Right, out typeName);

            case AliasQualifiedNameSyntax aliasQualified:
                return TryGetSimpleTypeName(aliasQualified.Name, out typeName);

            default:
                return false;
        }
    }
}
