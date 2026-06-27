using System.Collections.Immutable;

namespace MyEfVibe.Tests;

public sealed class ScriptPathResolverTests : IDisposable
{
    private readonly string _tempDirectory;

    public ScriptPathResolverTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "efvibe-script-path-" + Guid.NewGuid().ToString("N"));
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
    public void ResolvePath_combines_relative_path_with_base()
    {
        var resolved = ScriptPathResolver.ResolvePath("scripts/helpers.csx", _tempDirectory);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(_tempDirectory, "scripts/helpers.csx")),
            resolved);
    }

    [Fact]
    public void ResolveExistingFile_finds_file_in_search_path()
    {
        var helperPath = Path.Combine(_tempDirectory, "Helpers.csx");
        File.WriteAllText(helperPath, "int LoadedMagic = 1;");

        var resolved = ScriptPathResolver.ResolveExistingFile(
            "Helpers.csx",
            ImmutableArray.Create(_tempDirectory),
            _tempDirectory);

        Assert.Equal(Path.GetFullPath(helperPath), resolved);
    }

    [Fact]
    public void ResolveExistingFile_returns_null_when_missing()
    {
        var resolved = ScriptPathResolver.ResolveExistingFile(
            "Missing.csx",
            ImmutableArray.Create(_tempDirectory),
            _tempDirectory);

        Assert.Null(resolved);
    }

    [Fact]
    public void BuildLoadBootstrap_formats_absolute_paths()
    {
        var helperPath = Path.Combine(_tempDirectory, "Helpers.csx");

        var bootstrap = ScriptPathResolver.BuildLoadBootstrap([helperPath]);

        Assert.Contains("#load \"", bootstrap, StringComparison.Ordinal);
        Assert.Contains("Helpers.csx", bootstrap, StringComparison.Ordinal);
    }

    [Fact]
    public void EscapeForLoadDirective_escapes_backslashes_and_quotes()
    {
        var escaped = ScriptPathResolver.EscapeForLoadDirective(@"C:\scripts\helper"".csx");

        Assert.Contains(@"C:\\scripts\\", escaped, StringComparison.Ordinal);
        Assert.Contains(@"\""", escaped, StringComparison.Ordinal);
    }
}
