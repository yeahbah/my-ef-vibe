using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyEfVibe;

/// <summary>
///     Replaces open generic type parameters (for example <c>Set&lt;T&gt;()</c>) with a concrete entity
///     type so deep-scan probes compile in the REPL.
/// </summary>
internal static class OpenGenericProbeBinder
{
    internal static string Bind(string probeExpression, string concreteEntityTypeName)
    {
        if (string.IsNullOrWhiteSpace(probeExpression)
            || string.IsNullOrWhiteSpace(concreteEntityTypeName))
        {
            return probeExpression;
        }

        try
        {
            var singleLine = ProbeScriptFormatter.ToScriptExpression(probeExpression);
            var wrapped = $"var __efProbe = {singleLine};";

            var tree = CSharpSyntaxTree.ParseText(
                wrapped,
                CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));

            var rewritten = new OpenGenericTypeArgumentRewriter(concreteEntityTypeName).Visit(tree.GetRoot());

            if (rewritten is null)
            {
                return singleLine;
            }

            var text = rewritten.ToFullString();

            return UnwrapProbeAssignment(text) ?? singleLine;
        }
        catch (Exception)
        {
            return ProbeScriptFormatter.ToScriptExpression(probeExpression);
        }
    }

    internal static bool ContainsOpenGenericTypeParameter(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        try
        {
            var tree = CSharpSyntaxTree.ParseText(
                ProbeScriptFormatter.ToScriptExpression(expression),
                CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));

            return tree.GetRoot()
                .DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Any(static node =>
                    node.Parent is TypeArgumentListSyntax
                    && IsOpenTypeParameterName(node.Identifier.Text));
        }
        catch (Exception)
        {
            return expression.Contains("Set<T>", StringComparison.Ordinal)
                   || expression.Contains("<T>", StringComparison.Ordinal);
        }
    }

    internal static bool IsOpenTypeParameterName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (string.Equals(name, "T", StringComparison.Ordinal))
        {
            return true;
        }

        if (!name.StartsWith('T') || name.Length < 2)
        {
            return false;
        }

        return char.IsUpper(name[1]);
    }

    private static string? UnwrapProbeAssignment(string rewritten)
    {
        const string prefix = "var __efProbe = ";

        var index = rewritten.IndexOf(prefix, StringComparison.Ordinal);

        if (index < 0)
        {
            return rewritten.Trim();
        }

        var start = index + prefix.Length;
        var end = rewritten.LastIndexOf(';');

        if (end <= start)
        {
            return rewritten[start..].Trim();
        }

        return rewritten[start..end].Trim();
    }

    private sealed class OpenGenericTypeArgumentRewriter(string concreteEntityTypeName) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (node.Parent is TypeArgumentListSyntax
                && IsOpenTypeParameterName(node.Identifier.Text))
            {
                return SyntaxFactory.IdentifierName(concreteEntityTypeName)
                    .WithTriviaFrom(node);
            }

            return base.VisitIdentifierName(node);
        }
    }
}