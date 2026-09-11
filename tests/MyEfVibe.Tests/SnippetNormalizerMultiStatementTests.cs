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

    [Fact]
    public void ForEvaluation_caps_each_materializer_in_semicolon_separated_queries()
    {
        const string snippet = """
            db.Users.ToList();
            db.Users.Where(u => u.Id > 0).ToList();
            """;

        var normalized = SnippetNormalizer.ForEvaluation(snippet, typeof(FakeRewriterDbContext));

        ProbeTestHelper.AssertParsesAsScript(normalized);
        Assert.Equal(2, CountOccurrences(normalized, "ReplQueryableRuntime.Take("));
        Assert.Equal(2, CountOccurrences(normalized, ", 100)"));
        Assert.Equal(2, CountOccurrences(normalized, "ReplQueryableRuntime.ToList("));
        Assert.DoesNotContain("ToList(db.Users.ToList()", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void ForEvaluation_does_not_wrap_two_db_queries_as_one_runtime_call()
    {
        const string snippet = "db.Users.ToList(); db.Users.Where(u => u.Id > 0).ToList()";

        var normalized = SnippetNormalizer.ForEvaluation(snippet, typeof(FakeRewriterDbContext));

        ProbeTestHelper.AssertParsesAsScript(normalized);
        Assert.DoesNotContain("ToList(db.Users.ToList()", normalized, StringComparison.Ordinal);
        Assert.Contains("ReplQueryableRuntime.ToList(", normalized, StringComparison.Ordinal);
        Assert.Contains("ReplQueryableRuntime.Take(", normalized, StringComparison.Ordinal);
        Assert.Contains(", 100)", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void ForEvaluation_still_rewrites_multiline_single_query()
    {
        const string snippet = """
            db.Users
                .Where(u => u.Id > 0)
                .ToList()
            """;

        var normalized = SnippetNormalizer.ForEvaluation(snippet, typeof(FakeRewriterDbContext));

        ProbeTestHelper.AssertParsesAsScript(normalized);
        Assert.Contains("ReplQueryableRuntime.Take(", normalized, StringComparison.Ordinal);
        Assert.Contains(", 100)", normalized, StringComparison.Ordinal);
        Assert.Contains("ReplQueryableRuntime.ToList(", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRewriteToEfStaticCalls_rejects_semicolon_separated_queries()
    {
        var rewritten = EfReplQueryableRewriter.TryRewriteToEfStaticCalls(
            "db.Users.ToList(); db.Users.Where(u => u.Id > 0).ToList()",
            typeof(FakeRewriterDbContext));

        Assert.Null(rewritten);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var start = 0;

        while (true)
        {
            var index = text.IndexOf(value, start, StringComparison.Ordinal);

            if (index < 0)
            {
                return count;
            }

            count++;
            start = index + value.Length;
        }
    }
}
