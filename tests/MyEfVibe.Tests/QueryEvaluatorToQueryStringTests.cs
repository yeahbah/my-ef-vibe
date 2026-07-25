using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.EntityFrameworkCore;
using MyEfVibe.Linq;
using MyEfVibe.Workspace;

namespace MyEfVibe.Tests;

public sealed class QueryEvaluatorToQueryStringTests
{
    [Fact]
    public async Task EvaluateAsync_ToQueryString_preserves_session_filter_constant()
    {
        await using var context = await CreateContextAsync();
        using var assemblyLoader = CreateAssemblyLoader();
        var session = CreateSession(context, assemblyLoader);

        await session.EvaluateAsync("""var filter = "Bike";""");

        // Serve/REPL always pass a WorkspaceHost. The previous eval short-circuit stripped
        // `.ToQueryString()` and ran the remainder through LinqDeepSqlTranslator, which stubs
        // unbound identifiers and turned `filter` into an empty string.
        var host = (WorkspaceHost)RuntimeHelpers.GetUninitializedObject(typeof(WorkspaceHost));

        var (result, metrics) = await QueryEvaluator.EvaluateAsync(
            context,
            session,
            "db.Products.Where(product => product.Name == filter).ToQueryString()",
            new DbLogSettings { Enabled = false },
            [typeof(QueryCompProductDbContext).Assembly],
            host: host);

        Assert.True(metrics.Succeeded);
        Assert.IsType<string>(result);
        var sqlText = (string)result!;
        Assert.Contains("Bike", sqlText, StringComparison.Ordinal);
        Assert.DoesNotContain("''", sqlText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateAsync_ToQueryString_preserves_session_take_constant()
    {
        await using var context = await CreateContextAsync();
        using var assemblyLoader = CreateAssemblyLoader();
        var session = CreateSession(context, assemblyLoader);

        await session.EvaluateAsync("const int DefaultTake = 25;");

        var host = (WorkspaceHost)RuntimeHelpers.GetUninitializedObject(typeof(WorkspaceHost));

        var (result, metrics) = await QueryEvaluator.EvaluateAsync(
            context,
            session,
            "db.Products.OrderBy(product => product.Name).Take(DefaultTake).ToQueryString()",
            new DbLogSettings { Enabled = false },
            [typeof(QueryCompProductDbContext).Assembly],
            host: host);

        Assert.True(metrics.Succeeded);
        Assert.IsType<string>(result);
        var sqlText = (string)result!;
        Assert.Contains("25", sqlText, StringComparison.Ordinal);
        Assert.DoesNotContain("LIMIT 0", sqlText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeepTranslate_heuristics_reject_bare_session_variable_expression()
    {
        // Documents why eval must not short-circuit stripped expressions like `q` into deep translate.
        Assert.False(LinqEfQueryHeuristics.LooksLikeEfQuery("q"));
    }

    private static async Task<QueryCompProductDbContext> CreateContextAsync()
    {
        var options = new DbContextOptionsBuilder<QueryCompProductDbContext>()
            .UseSqlite($"Data Source=toquerystring-{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
            .Options;

        var context = new QueryCompProductDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static InteractiveAssemblyLoader CreateAssemblyLoader()
    {
        var assemblyLoader = new InteractiveAssemblyLoader();
        assemblyLoader.RegisterDependency(typeof(QueryCompProductDbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(DbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(ReplQueryableRuntime).Assembly);
        return assemblyLoader;
    }

    private static ScriptSession CreateSession(
        QueryCompProductDbContext context,
        InteractiveAssemblyLoader assemblyLoader)
    {
        var workspaceAssemblyPaths = new[]
            {
                typeof(QueryCompProductDbContext).Assembly.Location,
                typeof(DbContext).Assembly.Location,
                typeof(ReplQueryableRuntime).Assembly.Location,
            }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        return new ScriptSession(
            typeof(QueryCompProductDbContext),
            context,
            workspaceAssemblyPaths,
            assemblyLoader);
    }
}
