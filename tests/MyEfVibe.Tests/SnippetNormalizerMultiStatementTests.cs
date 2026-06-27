namespace MyEfVibe.Tests;

public sealed class SnippetNormalizerMultiStatementTests
{
    [Fact]
    public void ForEvaluation_preserves_var_assignment_followed_by_console_writeline()
    {
        const string snippet = """
            var first = x.FirstOrDefault();
            Console.WriteLine(first.Name);
            """;

        var normalized = SnippetNormalizer.ForEvaluation(
            snippet,
            typeof(FakeRewriterDbContext));

        Assert.Contains("var first =", normalized, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefault", normalized, StringComparison.Ordinal);
        Assert.Contains("Console.WriteLine(first.Name);", normalized, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(normalized);
    }

    [Fact]
    public void ForEvaluation_does_not_split_var_assignment_with_embedded_statement_on_one_line()
    {
        const string snippet = "var first = x.FirstOrDefault(); Console.WriteLine(first.Name);";

        var normalized = SnippetNormalizer.ForEvaluation(
            snippet,
            typeof(FakeRewriterDbContext));

        Assert.Contains("Console.WriteLine(first.Name);", normalized, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(normalized);
    }
}
