using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MyEfVibe.Tests;

internal static class ProbeTestHelper
{
    internal static void AssertParsesAsScript(string expression)
    {
        var tree = CSharpSyntaxTree.ParseText(
            expression,
            CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));

        var errors = tree.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.ToString())
            .ToArray();

        Assert.Empty(errors);
    }

    internal static string CollapseWhitespace(string value) =>
        string.Join(
            ' ',
            value.ReplaceLineEndings(" ")
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
