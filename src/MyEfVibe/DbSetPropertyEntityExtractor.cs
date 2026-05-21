using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyEfVibe;

/// <summary>
/// Resolves entity CLR type names from <c>DbContext.SomeDbSet</c> chains (not only <c>Set&lt;T&gt;()</c>).
/// </summary>
internal static class DbSetPropertyEntityExtractor
{
    private static readonly string[] ContextIdentifierNames = ["db", "DbContext", "_dbContext", "_db"];

    internal static bool TryExtractConcreteEntityTypeName(string code, Type dbContextType, out string entityTypeName)
    {
        entityTypeName = string.Empty;

        if (string.IsNullOrWhiteSpace(code))
            return false;

        try
        {
            var tree = CSharpSyntaxTree.ParseText(
                ProbeScriptFormatter.ToScriptExpression(code),
                CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));

            foreach (var memberAccess in tree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                if (!IsContextRoot(memberAccess.Expression))
                    continue;

                var propertyName = memberAccess.Name.Identifier.Text;

                if (!TryResolveDbSetEntityName(dbContextType, propertyName, out var resolved))
                    continue;

                entityTypeName = resolved;
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }

    private static bool IsContextRoot(ExpressionSyntax expression) =>
        expression switch
        {
            IdentifierNameSyntax identifier => ContextIdentifierNames.Contains(
                identifier.Identifier.Text,
                StringComparer.Ordinal),
            MemberAccessExpressionSyntax { Name.Identifier.Text: "DbContext" } => true,
            _ => false,
        };

    private static bool TryResolveDbSetEntityName(Type dbContextType, string propertyName, out string entityTypeName)
    {
        entityTypeName = string.Empty;

        for (var walk = dbContextType; walk is not null; walk = walk.BaseType)
        {
            var property = walk.GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.DeclaredOnly);

            if (property is null)
                continue;

            if (!property.PropertyType.IsGenericType)
                break;

            if (!typeof(System.Linq.IQueryable).IsAssignableFrom(property.PropertyType))
                break;

            var elementType = property.PropertyType.GetGenericArguments()[0];

            if (string.IsNullOrWhiteSpace(elementType.Name))
                break;

            entityTypeName = elementType.Name;
            return true;
        }

        return false;
    }
}
