namespace MyEfVibe.Tests;

public sealed class SnippetNormalizerStatementTerminatorTests
{
    [Theory]
    [InlineData("using System", "using System;")]
    [InlineData("using System;", "using System;")]
    [InlineData("var x = db.Products", "var x = db.Products;")]
    public void ForEvaluation_normalizes_statement_terminators(string snippet, string expected)
    {
        var normalized = SnippetNormalizer.ForEvaluation(
            snippet,
            typeof(FakeRewriterDbContext));

        Assert.Equal(expected, normalized);
    }
}
