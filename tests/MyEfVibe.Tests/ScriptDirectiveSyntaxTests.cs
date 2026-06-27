namespace MyEfVibe.Tests;

public sealed class ScriptDirectiveSyntaxTests
{
    [Theory]
    [InlineData("#load \"helpers.csx\"", true)]
    [InlineData("  #load \"helpers.csx\"", true)]
    [InlineData("#r \"MyLib.dll\"", true)]
    [InlineData("db.Products.Count()", false)]
    [InlineData("using System.Linq;", false)]
    public void IsScriptDirectiveLine_detects_directives(string line, bool expected)
    {
        Assert.Equal(expected, ScriptDirectiveSyntax.IsScriptDirectiveLine(line));
    }

    [Fact]
    public void ContainsScriptDirectives_returns_true_when_any_line_is_directive()
    {
        const string snippet = """
            var count = 1;
            #load "helpers.csx"
            db.Products.Take(count)
            """;

        Assert.True(ScriptDirectiveSyntax.ContainsScriptDirectives(snippet));
    }

    [Fact]
    public void ContainsScriptDirectives_returns_false_for_plain_linq()
    {
        Assert.False(ScriptDirectiveSyntax.ContainsScriptDirectives("db.Products.Take(5).ToList()"));
    }
}
