using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyEfVibe;

internal enum DbContextTypeClassification
{
    None,
    Selected,
    Other
}

internal static class DbContextTypeNameSyntax
{
    internal static bool TryGetSimpleTypeName(TypeSyntax? typeSyntax, out string typeName)
    {
        typeName = string.Empty;

        switch (typeSyntax)
        {
            case null:
                return false;

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

    internal static DbContextTypeClassification Classify(string typeName, DbContextScanScope scope)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return DbContextTypeClassification.None;
        }

        if (scope.IsSelectedContextType(typeName))
        {
            return DbContextTypeClassification.Selected;
        }

        if (scope.OtherContextTypeNames.Contains(typeName))
        {
            return DbContextTypeClassification.Other;
        }

        if (string.Equals(typeName, "DbContext", StringComparison.Ordinal) && !scope.HasMultipleContexts)
        {
            return DbContextTypeClassification.Selected;
        }

        return DbContextTypeClassification.None;
    }
}