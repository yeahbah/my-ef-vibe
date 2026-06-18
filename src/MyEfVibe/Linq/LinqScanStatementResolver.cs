using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyEfVibe.Linq;

/// <summary>
///     Resolves the EF query text associated with a LINQ invocation discovered during :scan.
/// </summary>
internal static class LinqScanStatementResolver
{
    internal static string Resolve(SyntaxNode node)
    {
        if (TryGetAnonymousObjectMemberQuery(node, out var memberQuery))
        {
            return memberQuery;
        }

        var statement = node.FirstAncestorOrSelf<StatementSyntax>();
        var text = statement?.ToString() ?? node.ToString();

        if (LinqEfQueryHeuristics.LooksLikeEfQuery(text))
        {
            return text;
        }

        var block = node.FirstAncestorOrSelf<BlockSyntax>();

        if (block is not null)
        {
            foreach (var sibling in block.Statements)
            {
                var siblingText = sibling.ToString();

                if (LinqEfQueryHeuristics.LooksLikeEfQuery(siblingText))
                {
                    return siblingText;
                }
            }
        }

        return text;
    }

    private static bool TryGetAnonymousObjectMemberQuery(SyntaxNode node, out string query)
    {
        query = string.Empty;

        var memberDeclarator = node.FirstAncestorOrSelf<AnonymousObjectMemberDeclaratorSyntax>();

        if (memberDeclarator is null)
        {
            return false;
        }

        var expressionText = memberDeclarator.Expression.ToString().Trim();

        if (!LinqEfQueryHeuristics.LooksLikeEfQuery(expressionText))
        {
            return false;
        }

        query = expressionText;
        return true;
    }
}
