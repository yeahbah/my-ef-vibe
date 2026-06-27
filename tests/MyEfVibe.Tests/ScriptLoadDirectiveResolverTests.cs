namespace MyEfVibe.Tests;

public sealed class ScriptLoadDirectiveResolverTests
{
    [Fact]
    public void ResolveDirectives_expands_relative_load_path()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "efvibe-load-resolve-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var helperPath = Path.Combine(tempDirectory, "Helpers.csx");
        File.WriteAllText(helperPath, "int LoadedMagic = 21;");

        try
        {
            var resolved = ScriptLoadDirectiveResolver.ResolveDirectives(
                "#load \"Helpers.csx\"",
                [tempDirectory],
                tempDirectory);

            Assert.StartsWith("#load \"", resolved, StringComparison.Ordinal);
            Assert.Contains("Helpers.csx", resolved, StringComparison.Ordinal);
            Assert.DoesNotContain("#load \"Helpers.csx\"", resolved, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void TryParseLoadPath_parses_quoted_directive()
    {
        Assert.Equal("Helpers.csx", ScriptLoadDirectiveResolver.TryParseLoadPath("#load \"Helpers.csx\""));
    }
}
