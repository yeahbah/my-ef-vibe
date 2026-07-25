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
    public async Task EvaluateAsync_ToQueryString_on_session_variable_uses_live_session()
    {
        await using var context = await CreateContextAsync();
        using var assemblyLoader = CreateAssemblyLoader();
        var session = CreateSession(context, assemblyLoader);

        await session.EvaluateAsync("var q = db.Products.Where(product => product.ListPrice > 0);");

        // Serve/REPL always pass a WorkspaceHost. The previous eval short-circuit sent the stripped
        // expression (`q`) through LinqDeepSqlTranslator, which rejects bare session variables.
        var host = (WorkspaceHost)RuntimeHelpers.GetUninitializedObject(typeof(WorkspaceHost));

        var (result, metrics) = await QueryEvaluator.EvaluateAsync(
            context,
            session,
            "q.ToQueryString()",
            new DbLogSettings { Enabled = false },
            [typeof(QueryCompProductDbContext).Assembly],
            host: host);

        Assert.True(metrics.Succeeded);
        Assert.IsType<string>(result);
        var sqlText = (string)result!;
        Assert.Contains("Product", sqlText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ListPrice", sqlText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvaluateAsync_ToQueryString_preserves_session_filter_constant()
    {
        await using var context = await CreateContextAsync();
        using var assemblyLoader = CreateAssemblyLoader();
        var session = CreateSession(context, assemblyLoader);

        await session.EvaluateAsync("""var filter = "Bike";""");

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
    public async Task DeepTranslate_rejects_bare_session_variable_expression()
    {
        await using var context = await CreateContextAsync();
        using var assemblyLoader = CreateAssemblyLoader();
        var session = CreateSession(context, assemblyLoader);

        await session.EvaluateAsync("var q = db.Products.Where(product => product.ListPrice > 0);");

        var host = (WorkspaceHost)RuntimeHelpers.GetUninitializedObject(typeof(WorkspaceHost));

        var translation = await LinqDeepSqlTranslator.TranslateAsync(session, host, "q");

        Assert.Null(translation.Sql);
        Assert.Equal("Not an EF Core query expression.", translation.Note);
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
