using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyEfVibe;

internal static class SetEntityTypeExtractor
{
    internal static bool TryExtractConcreteEntityTypeName(string code, out string entityTypeName)
    {
        entityTypeName = string.Empty;

        if (string.IsNullOrWhiteSpace(code))
            return false;

        try
        {
            var tree = CSharpSyntaxTree.ParseText(
                ProbeScriptFormatter.ToScriptExpression(code),
                CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));

            foreach (var genericName in tree.GetRoot().DescendantNodes().OfType<GenericNameSyntax>())
            {
                if (!string.Equals(genericName.Identifier.Text, "Set", StringComparison.Ordinal))
                    continue;

                if (genericName.TypeArgumentList.Arguments.Count != 1)
                    continue;

                if (!TryGetConcreteTypeName(genericName.TypeArgumentList.Arguments[0], out var typeName))
                    continue;

                entityTypeName = typeName;
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }

    private static bool TryGetConcreteTypeName(TypeSyntax typeSyntax, out string typeName)
    {
        typeName = string.Empty;

        switch (typeSyntax)
        {
            case IdentifierNameSyntax identifier
                when !OpenGenericProbeBinder.IsOpenTypeParameterName(identifier.Identifier.Text):
                typeName = identifier.Identifier.Text;
                return true;

            case QualifiedNameSyntax qualified:
                return TryGetConcreteTypeName(qualified.Right, out typeName);

            case AliasQualifiedNameSyntax aliasQualified:
                return TryGetConcreteTypeName(aliasQualified.Name, out typeName);

            default:
                return false;
        }
    }
}
