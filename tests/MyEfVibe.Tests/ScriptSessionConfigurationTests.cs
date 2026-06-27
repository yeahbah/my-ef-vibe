namespace MyEfVibe.Tests;

public sealed class ScriptSessionConfigurationTests : IDisposable
{
    private readonly string _tempDirectory;

    public ScriptSessionConfigurationTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "efvibe-script-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void FromCli_splits_semicolon_and_comma_separated_values()
    {
        var configuration = ScriptSessionConfiguration.FromCli(
            ["./scripts", "./shared"],
            ["helpers.csx; filters.csx", "constants.csx"],
            ["MyApp.Helpers;System.Globalization", "System.Text"],
            _tempDirectory);

        Assert.Equal(["./scripts", "./shared"], configuration.SearchPaths);
        Assert.Equal(3, configuration.LoadPaths.Count);
        Assert.Contains("helpers.csx", configuration.LoadPaths);
        Assert.Contains("filters.csx", configuration.LoadPaths);
        Assert.Contains("constants.csx", configuration.LoadPaths);
        Assert.Equal(3, configuration.AdditionalUsings.Count);
        Assert.Contains("MyApp.Helpers", configuration.AdditionalUsings);
        Assert.Contains("System.Globalization", configuration.AdditionalUsings);
        Assert.Contains("System.Text", configuration.AdditionalUsings);
    }

    [Fact]
    public void FromCli_strips_trailing_semicolons_from_usings()
    {
        var configuration = ScriptSessionConfiguration.FromCli(
            null,
            null,
            ["MyApp.Helpers;", "System.Text;"],
            null);

        Assert.Equal(["MyApp.Helpers", "System.Text"], configuration.AdditionalUsings);
    }

    [Fact]
    public void ResolveSearchPaths_includes_configured_and_fallback_paths()
    {
        var scriptsDirectory = Path.Combine(_tempDirectory, "scripts");
        Directory.CreateDirectory(scriptsDirectory);

        var configuration = new ScriptSessionConfiguration
        {
            SearchPaths = [scriptsDirectory],
            BasePath = _tempDirectory
        };

        var resolved = configuration.ResolveSearchPaths(_tempDirectory);

        Assert.Equal(2, resolved.Length);
        Assert.Equal(Path.GetFullPath(scriptsDirectory), resolved[0]);
        Assert.Equal(Path.GetFullPath(_tempDirectory), resolved[1]);
    }

    [Fact]
    public void ResolveLoadPaths_resolves_relative_files_against_search_paths()
    {
        var helperPath = Path.Combine(_tempDirectory, "Helpers.csx");
        File.WriteAllText(helperPath, "int LoadedMagic = 1;");

        var configuration = new ScriptSessionConfiguration
        {
            LoadPaths = ["Helpers.csx"],
            SearchPaths = [_tempDirectory]
        };

        var resolved = configuration.ResolveLoadPaths(_tempDirectory);

        Assert.Single(resolved);
        Assert.Equal(Path.GetFullPath(helperPath), resolved[0]);
    }

    [Fact]
    public void ResolveLoadPaths_deduplicates_same_file()
    {
        var helperPath = Path.Combine(_tempDirectory, "Helpers.csx");
        File.WriteAllText(helperPath, "int LoadedMagic = 1;");

        var configuration = new ScriptSessionConfiguration
        {
            LoadPaths = ["Helpers.csx", "./Helpers.csx"],
            SearchPaths = [_tempDirectory]
        };

        var resolved = configuration.ResolveLoadPaths(_tempDirectory);

        Assert.Single(resolved);
    }

    [Fact]
    public void ResolveBasePath_prefers_explicit_base_path()
    {
        var scriptsDirectory = Path.Combine(_tempDirectory, "scripts");
        Directory.CreateDirectory(scriptsDirectory);

        var configuration = new ScriptSessionConfiguration
        {
            SearchPaths = [scriptsDirectory],
            BasePath = _tempDirectory
        };

        Assert.Equal(Path.GetFullPath(_tempDirectory), configuration.ResolveBasePath(_tempDirectory));
    }
}
