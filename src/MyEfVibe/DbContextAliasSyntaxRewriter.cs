using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyEfVibe;

/// <summary>
/// Rewrites DbContext instance identifiers to the REPL global <c>db</c> for deep-scan SQL translation.
/// </summary>
internal sealed class DbContextAliasSyntaxRewriter : CSharpSyntaxRewriter
{
    private readonly HashSet<string> _replaceableIdentifiers;

    private DbContextAliasSyntaxRewriter(IEnumerable<string> replaceableIdentifiers)
    {
        _replaceableIdentifiers = new HashSet<string>(replaceableIdentifiers, StringComparer.Ordinal);
        _replaceableIdentifiers.Add("db");
    }

    internal static string Rewrite(string code, IEnumerable<string>? contextInstanceIdentifiers = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            return code;

        var identifiers = BuildReplaceableIdentifiers(contextInstanceIdentifiers);

        if (!MightContainReplaceableReference(code, identifiers))
            return code;

        var tree = CSharpSyntaxTree.ParseText(code, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
        var rewritten = new DbContextAliasSyntaxRewriter(identifiers).Visit(tree.GetRoot());

        return rewritten.ToFullString();
    }

    private static HashSet<string> BuildReplaceableIdentifiers(IEnumerable<string>? contextInstanceIdentifiers)
    {
        var identifiers = new HashSet<string>(DbContextQueryMarkers.BuiltInReplaceableIdentifiers, StringComparer.Ordinal);

        if (contextInstanceIdentifiers is not null)
        {
            foreach (var identifier in contextInstanceIdentifiers)
            {
                if (!string.IsNullOrWhiteSpace(identifier))
                    identifiers.Add(identifier);
            }
        }

        return identifiers;
    }

    private static bool MightContainReplaceableReference(string code, HashSet<string> identifiers)
    {
        if (code.Contains("db.", StringComparison.Ordinal))
            return true;

        foreach (var identifier in identifiers)
        {
            if (identifier.Equals("db", StringComparison.Ordinal))
                continue;

            if (code.Contains(identifier, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (node.Expression is ThisExpressionSyntax
            && _replaceableIdentifiers.Contains(node.Name.Identifier.Text))
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

        if (_replaceableIdentifiers.Contains(identifier) && ShouldReplaceDbContextIdentifier(node))
            return SyntaxFactory.IdentifierName("db");

        return base.VisitIdentifierName(node);
    }

    private static bool ShouldReplaceDbContextIdentifier(IdentifierNameSyntax node) =>
        !IsParameterReference(node)
        && !IsLambdaParameter(node)
        && (IsMemberAccessReceiver(node) || IsInvocationTarget(node));

    private static bool IsMemberAccessReceiver(IdentifierNameSyntax node) =>
        node.Parent is MemberAccessExpressionSyntax memberAccess
        && memberAccess.Expression == node;

    private static bool IsInvocationTarget(IdentifierNameSyntax node) =>
        node.Parent is InvocationExpressionSyntax invocation
        && invocation.Expression == node;

    private static bool IsParameterReference(IdentifierNameSyntax node)
    {
        var name = node.Identifier.Text;

        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is ParameterSyntax parameter && parameter.Identifier.Text == name)
                return true;
        }

        return false;
    }

    private static bool IsLambdaParameter(IdentifierNameSyntax node)
    {
        var name = node.Identifier.Text;

        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is SimpleLambdaExpressionSyntax simple
                && simple.Parameter is { } simpleParameter
                && simpleParameter.Identifier.Text == name)
                return true;

            if (current is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax)
                break;
        }

        return false;
    }
}
