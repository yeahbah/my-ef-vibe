using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.EntityFrameworkCore;
using MyEfVibeScriptUsings;

namespace MyEfVibe.Tests;

public sealed class ScriptLoadTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _helperPath;

    public ScriptLoadTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "efvibe-script-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _helperPath = Path.Combine(_tempDirectory, "Helpers.csx");
        File.WriteAllText(_helperPath, "int LoadedMagic = 21;");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task InitializeAsync_loads_configured_script_file()
    {
        var session = await CreateSessionAsync(
            new ScriptSessionConfiguration
            {
                LoadPaths = ["Helpers.csx"],
                SearchPaths = [_tempDirectory]
            });

        var result = await session.EvaluateAsync("LoadedMagic * 2");

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task EvaluateAsync_supports_inline_load_directive()
    {
        var session = await CreateSessionAsync(
            new ScriptSessionConfiguration
            {
                SearchPaths = [_tempDirectory]
            });

        var result = await session.EvaluateAsync(
            "#load \"Helpers.csx\"\nLoadedMagic + 1");

        Assert.Equal(22, result);
    }

    [Fact]
    public async Task AdditionalUsings_are_available_in_session()
    {
        var session = await CreateSessionAsync(
            new ScriptSessionConfiguration
            {
                AdditionalUsings = ["MyEfVibeScriptUsings"]
            });

        var result = await session.EvaluateAsync("QueryMath.Double(11)");

        Assert.Equal(22, result);
    }

    [Fact]
    public async Task InitializeAsync_throws_when_load_file_is_missing()
    {
        var session = await CreateSessionWithoutInitializeAsync(
            new ScriptSessionConfiguration
            {
                LoadPaths = ["Missing.csx"],
                SearchPaths = [_tempDirectory]
            });

        var failure = await Assert.ThrowsAsync<FileNotFoundException>(
            () => session.InitializeAsync(_tempDirectory));

        Assert.Contains("Missing.csx", failure.Message, StringComparison.Ordinal);
    }

    private async Task<ScriptSession> CreateSessionAsync(ScriptSessionConfiguration configuration)
    {
        var session = await CreateSessionWithoutInitializeAsync(configuration);
        await session.InitializeAsync(_tempDirectory);
        return session;
    }

    private async Task<ScriptSession> CreateSessionWithoutInitializeAsync(ScriptSessionConfiguration configuration)
    {
        var options = new DbContextOptionsBuilder<ProbeHistoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new ProbeHistoryDbContext(options);
        var assemblyLoader = new InteractiveAssemblyLoader();

        assemblyLoader.RegisterDependency(typeof(ProbeHistoryDbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(DbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(QueryMath).Assembly);

        var workspaceAssemblyPaths = new[]
            {
                typeof(ProbeHistoryDbContext).Assembly.Location,
                typeof(DbContext).Assembly.Location,
                typeof(QueryMath).Assembly.Location
            }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        var session = new ScriptSession(
            typeof(ProbeHistoryDbContext),
            context,
            workspaceAssemblyPaths,
            assemblyLoader,
            configuration: configuration,
            scriptSearchBasePath: _tempDirectory);

        return await Task.FromResult(session);
    }
}
