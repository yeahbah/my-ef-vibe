using System.Collections;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class QueryEvaluatorAsyncMaterializationTests
{
    [Theory]
    [InlineData("db.Users.ToListAsync()")]
    [InlineData("await db.Users.ToListAsync()")]
    public async Task EvaluateAsync_caps_unbounded_async_list_materializers_at_100(string snippet)
    {
        await using var context = await CreateSeededContextAsync(rowCount: 150);
        using var assemblyLoader = CreateAssemblyLoader();
        var session = new ScriptSession(
            typeof(ProbeHistoryDbContext),
            context,
            CreateWorkspaceAssemblyPaths(),
            assemblyLoader);

        var (result, metrics) = await QueryEvaluator.EvaluateAsync(
            context,
            session,
            snippet,
            new DbLogSettings { Enabled = false },
            [typeof(ProbeHistoryDbContext).Assembly]);

        Assert.Equal(100, CountRows(result));
        Assert.Equal(100, metrics.RowCount);
        Assert.Contains(
            metrics.Warnings,
            warning => warning.Contains("100 rows", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateAsync_sync_ToList_still_caps_at_100()
    {
        await using var context = await CreateSeededContextAsync(rowCount: 150);
        using var assemblyLoader = CreateAssemblyLoader();
        var session = new ScriptSession(
            typeof(ProbeHistoryDbContext),
            context,
            CreateWorkspaceAssemblyPaths(),
            assemblyLoader);

        var (result, metrics) = await QueryEvaluator.EvaluateAsync(
            context,
            session,
            "db.Users.ToList()",
            new DbLogSettings { Enabled = false },
            [typeof(ProbeHistoryDbContext).Assembly]);

        Assert.Equal(100, CountRows(result));
        Assert.Equal(100, metrics.RowCount);
        Assert.Contains(
            metrics.Warnings,
            warning => warning.Contains("100 rows", StringComparison.Ordinal));
    }

    private static int CountRows(object? result)
    {
        return result is ICollection collection
            ? collection.Count
            : result is IEnumerable enumerable
                ? enumerable.Cast<object?>().Count()
                : 0;
    }

    private static async Task<ProbeHistoryDbContext> CreateSeededContextAsync(int rowCount)
    {
        var options = new DbContextOptionsBuilder<ProbeHistoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new ProbeHistoryDbContext(options);
        context.Users.AddRange(Enumerable.Range(1, rowCount).Select(id => new ProbeHistoryUser { Id = id }));
        await context.SaveChangesAsync();
        return context;
    }

    private static InteractiveAssemblyLoader CreateAssemblyLoader()
    {
        var assemblyLoader = new InteractiveAssemblyLoader();
        assemblyLoader.RegisterDependency(typeof(ProbeHistoryDbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(DbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(ReplQueryableRuntime).Assembly);
        return assemblyLoader;
    }

    private static ImmutableHashSet<string> CreateWorkspaceAssemblyPaths()
    {
        return new[]
            {
                typeof(ProbeHistoryDbContext).Assembly.Location,
                typeof(DbContext).Assembly.Location,
                typeof(ReplQueryableRuntime).Assembly.Location
            }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
