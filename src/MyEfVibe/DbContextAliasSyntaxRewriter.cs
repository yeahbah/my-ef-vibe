using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyEfVibe;

/// <summary>
/// Rewrites common injected DbContext parameter/field names to the REPL global <c>db</c> for deep-scan SQL translation.
/// </summary>
internal sealed class DbContextAliasSyntaxRewriter : CSharpSyntaxRewriter
{
    private static readonly HashSet<string> DbContextIdentifiers = new(StringComparer.Ordinal)
    {
        "dbContext",
        "_dbContext",
        "DbContext",
        "_context",
        "applicationDbContext",
        "_applicationDbContext",
        "appDbContext",
        "_appDbContext",
    };

    internal static string Rewrite(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return code;

        if (!MightContainDbContextReference(code))
            return code;

        var tree = CSharpSyntaxTree.ParseText(code, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
        var rewritten = new DbContextAliasSyntaxRewriter().Visit(tree.GetRoot());

        return rewritten.ToFullString();
    }

    private static bool MightContainDbContextReference(string code) =>
        code.Contains("Context", StringComparison.Ordinal)
        || code.Contains("context", StringComparison.Ordinal)
        || code.Contains("db.", StringComparison.Ordinal);

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (node.Expression is ThisExpressionSyntax
            && ShouldReplaceMemberName(node.Name.Identifier.Text))
        {
            return SyntaxFactory.IdentifierName("db");
        }

        return base.VisitMemberAccessExpression(node);
    }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        var identifier = node.Identifier.Text;

        if (string.Equals(identifier, "db", StringComparison.Ordinal))
            return base.VisitIdentifierName(node);

        if (identifier == "context" && ShouldReplaceContextIdentifier(node))
            return SyntaxFactory.IdentifierName("db");

        if (DbContextIdentifiers.Contains(identifier) && ShouldReplaceDbContextIdentifier(node))
            return SyntaxFactory.IdentifierName("db");

        return base.VisitIdentifierName(node);
    }

    private static bool ShouldReplaceMemberName(string name) =>
        DbContextIdentifiers.Contains(name)
        || string.Equals(name, "context", StringComparison.Ordinal);

    private static bool ShouldReplaceDbContextIdentifier(IdentifierNameSyntax node) =>
        IsMemberAccessReceiver(node) || IsInvocationTarget(node);

    private static bool ShouldReplaceContextIdentifier(IdentifierNameSyntax node) =>
        !IsLambdaParameter(node) && (IsMemberAccessReceiver(node) || IsInvocationTarget(node));

    private static bool IsMemberAccessReceiver(IdentifierNameSyntax node) =>
        node.Parent is MemberAccessExpressionSyntax memberAccess
        && memberAccess.Expression == node;

    private static bool IsInvocationTarget(IdentifierNameSyntax node) =>
        node.Parent is InvocationExpressionSyntax invocation
        && invocation.Expression == node;

    private static bool IsLambdaParameter(IdentifierNameSyntax node)
    {
        var name = node.Identifier.Text;

        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is SimpleLambdaExpressionSyntax simple
                && simple.Parameter is { } simpleParameter
                && simpleParameter.Identifier.Text == name)
                return true;

            if (current is ParameterSyntax parameter && parameter.Identifier.Text == name)
                return true;

            if (current is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax)
                break;
        }

        return false;
    }
}
