using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.EntityFrameworkCore;
using MyEfVibe.Reporters;

namespace MyEfVibe.Tests;

public sealed class QueryEvaluatorDeferredEnumerationTests
{
    [Fact]
    public async Task EvaluateAsync_freezes_deferred_enumerables_before_json_reporting()
    {
        await using var context = CreateContext();
        using var assemblyLoader = CreateAssemblyLoader();
        var workspaceAssemblyPaths = CreateWorkspaceAssemblyPaths();
        var session = new ScriptSession(
            typeof(ProbeHistoryDbContext),
            context,
            workspaceAssemblyPaths,
            assemblyLoader);

        DeferredEnumerationProbe.Reset();

        var (result, metrics) = await QueryEvaluator.EvaluateAsync(
            context,
            session,
            "DeferredEnumerationProbe.CreateRows()",
            new DbLogSettings { Enabled = false },
            [typeof(ProbeHistoryDbContext).Assembly]);

        Assert.Equal(3, DeferredEnumerationProbe.MoveNextCount);

        var payload = EvaluationJsonReporter.BuildSuccess(result, metrics);

        Assert.Equal(3, DeferredEnumerationProbe.MoveNextCount);
        Assert.Equal("3 rows", payload.Value);
        Assert.NotNull(payload.Rows);
        Assert.Equal(3, payload.Rows!.Count);
    }

    private static ProbeHistoryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ProbeHistoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ProbeHistoryDbContext(options);
    }

    private static InteractiveAssemblyLoader CreateAssemblyLoader()
    {
        var assemblyLoader = new InteractiveAssemblyLoader();
        assemblyLoader.RegisterDependency(typeof(ProbeHistoryDbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(DbContext).Assembly);

        return assemblyLoader;
    }

    private static ImmutableHashSet<string> CreateWorkspaceAssemblyPaths()
    {
        return new[]
            {
                typeof(ProbeHistoryDbContext).Assembly.Location,
                typeof(DbContext).Assembly.Location
            }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

public static class DeferredEnumerationProbe
{
    public static int MoveNextCount { get; private set; }

    public static void Reset()
    {
        MoveNextCount = 0;
    }

    public static IEnumerable<int> CreateRows()
    {
        for (var index = 1; index <= 3; index++)
        {
            MoveNextCount++;
            yield return index;
        }
    }
}
