namespace MyEfVibe.Tests;

public sealed class SnippetNormalizerScriptDirectiveTests
{
    [Fact]
    public void ForEvaluation_preserves_multiline_load_directive_without_repository_rewrite()
    {
        const string snippet = """
            #load "Helpers.csx"
            db.Products.Take(5)
            """;

        var normalized = SnippetNormalizer.ForEvaluation(
            snippet,
            typeof(ProbeHistoryDbContext),
            preserveAsyncQueries: false);

        var (directives, body) = ScriptDirectiveSplitter.SplitLeadingDirectives(normalized);

        Assert.Equal("#load \"Helpers.csx\"", directives);
        Assert.Contains("db.Products.Take(5)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ReplQueryableRuntime", normalized, StringComparison.Ordinal);
    }
}
