using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyEfVibe;

/// <summary>
/// Maps containing type names to the concrete DbContext type they use (from ctor/field/base analysis).
/// </summary>
internal sealed class DbContextContainingTypeIndex
{
    private readonly Dictionary<string, string> _contextTypeByContainingType =
        new(StringComparer.Ordinal);

    internal static DbContextContainingTypeIndex Build(string sourceText, DbContextScanScope scope)
    {
        var index = new DbContextContainingTypeIndex();

        SyntaxTree tree;

        try
        {
            tree = CSharpSyntaxTree.ParseText(sourceText);
        }
        catch (Exception)
        {
            return index;
        }

        var root = tree.GetCompilationUnitRoot();

        foreach (var typeDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            var typeName = typeDeclaration.Identifier.Text;

            if (string.IsNullOrWhiteSpace(typeName))
                continue;

            var boundContext = TryResolveContextTypeForDeclaration(typeDeclaration, scope);

            if (boundContext is null)
                continue;

            index._contextTypeByContainingType[typeName] = boundContext;
        }

        return index;
    }

    internal bool TryGetBoundContextType(string? containingTypeName, out string contextTypeName)
    {
        contextTypeName = string.Empty;

        if (string.IsNullOrWhiteSpace(containingTypeName))
            return false;

        return _contextTypeByContainingType.TryGetValue(containingTypeName, out contextTypeName!);
    }

    private static string? TryResolveContextTypeForDeclaration(
        TypeDeclarationSyntax typeDeclaration,
        DbContextScanScope scope)
    {
        foreach (var parameter in typeDeclaration.ParameterList?.Parameters ?? [])
        {
            if (DbContextTypeNameSyntax.TryGetSimpleTypeName(parameter.Type, out var typeName)
                && IsKnownContextType(typeName, scope))
                return typeName;
        }

        foreach (var field in typeDeclaration.Members.OfType<FieldDeclarationSyntax>())
        {
            if (!DbContextTypeNameSyntax.TryGetSimpleTypeName(field.Declaration.Type, out var typeName))
                continue;

            if (IsKnownContextType(typeName, scope))
                return typeName;
        }

        foreach (var property in typeDeclaration.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (!DbContextTypeNameSyntax.TryGetSimpleTypeName(property.Type, out var typeName))
                continue;

            if (IsKnownContextType(typeName, scope))
                return typeName;
        }

        if (typeDeclaration.BaseList is not null)
        {
            foreach (var baseType in typeDeclaration.BaseList.Types)
            {
                if (DbContextTypeNameSyntax.TryGetSimpleTypeName(baseType.Type, out var baseTypeName)
                    && IsKnownContextType(baseTypeName, scope))
                    return baseTypeName;

                if (TryGetOtherContextFromTypeSyntax(baseType.Type, scope, out var otherContext))
                    return otherContext;

                if (InheritsRepositoryBase(baseType.Type))
                    return scope.SelectedContextTypeName;
            }
        }

        return null;
    }

    private static bool TryGetOtherContextFromTypeSyntax(
        TypeSyntax typeSyntax,
        DbContextScanScope scope,
        out string? otherContext)
    {
        var text = typeSyntax.ToString();

        foreach (var name in scope.OtherContextTypeNames)
        {
            if (!text.Contains(name, StringComparison.Ordinal))
                continue;

            otherContext = name;
            return true;
        }

        otherContext = null;
        return false;
    }

    private static bool InheritsRepositoryBase(TypeSyntax baseTypeSyntax)
    {
        var text = baseTypeSyntax.ToString();

        return text.Contains("ReadOnlyEfRepository", StringComparison.Ordinal)
               || text.Contains("EfRepository", StringComparison.Ordinal);
    }

    private static bool IsKnownContextType(string typeName, DbContextScanScope scope) =>
        DbContextTypeNameSyntax.Classify(typeName, scope) != DbContextTypeClassification.None;
}
