using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyEfVibe;

/// <summary>
/// Rewrites EF async query terminals to synchronous equivalents for Roslyn script evaluation.
/// </summary>
internal static class AsyncQueryableSyncRewriter
{
    private static readonly Dictionary<string, string> AsyncToSync = new(StringComparer.Ordinal)
    {
        ["ToListAsync"] = "ToList",
        ["ToArrayAsync"] = "ToArray",
        ["CountAsync"] = "Count",
        ["AnyAsync"] = "Any",
        ["FirstOrDefaultAsync"] = "FirstOrDefault",
        ["FirstAsync"] = "First",
        ["SingleOrDefaultAsync"] = "SingleOrDefault",
        ["SingleAsync"] = "Single",
        ["MaxAsync"] = "Max",
        ["MinAsync"] = "Min",
        ["AverageAsync"] = "Average",
        ["SumAsync"] = "Sum",
    };

    internal static string Rewrite(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression) || !expression.Contains("Async", StringComparison.Ordinal))
            return expression;

        try
        {
            var tree = CSharpSyntaxTree.ParseText(expression, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
            var rewritten = new AsyncInvocationRewriter().Visit(tree.GetRoot());

            return rewritten.ToFullString();
        }
        catch (Exception)
        {
            return expression;
        }
    }

    private sealed class AsyncInvocationRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var rewritten = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;

            if (rewritten.Expression is not MemberAccessExpressionSyntax memberAccess)
                return rewritten;

            var methodName = memberAccess.Name.Identifier.Text;

            if (!AsyncToSync.TryGetValue(methodName, out var syncName))
                return rewritten;

            var syncMember = memberAccess.WithName(SyntaxFactory.IdentifierName(syncName));
            return rewritten.WithExpression(syncMember);
        }
    }
}
