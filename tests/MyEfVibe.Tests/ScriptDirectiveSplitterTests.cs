namespace MyEfVibe.Tests;

public sealed class ScriptDirectiveSplitterTests
{
    [Fact]
    public void SplitLeadingDirectives_separates_load_from_body()
    {
        var (directives, body) = ScriptDirectiveSplitter.SplitLeadingDirectives(
            "#load \"Helpers.csx\"\nLoadedMagic + 1");

        Assert.Equal("#load \"Helpers.csx\"", directives);
        Assert.Equal("LoadedMagic + 1", body);
    }
}
