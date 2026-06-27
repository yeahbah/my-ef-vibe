namespace MyEfVibe.Tests;

public sealed class ScriptLoadNormalizationTests
{
    [Fact]
    public void ForEvaluation_preserves_load_directive_for_parsing()
    {
        var normalized = SnippetNormalizer.ForEvaluation(
            "#load \"Helpers.csx\"\nLoadedMagic + 1",
            typeof(ProbeHistoryDbContext),
            preserveAsyncQueries: false);

        var (directives, body) = ScriptDirectiveSplitter.SplitLeadingDirectives(normalized);

        Assert.Equal("#load \"Helpers.csx\"", directives);
        Assert.Equal("Helpers.csx", ScriptLoadDirectiveResolver.TryParseLoadPath(directives));
        Assert.Equal("LoadedMagic + 1", body);
    }
}
