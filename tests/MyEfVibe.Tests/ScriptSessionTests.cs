using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class ScriptSessionTests
{
    [Fact]
    public async Task EvaluateProbeAsync_does_not_record_probe_in_completion_history()
    {
        var options = new DbContextOptionsBuilder<ProbeHistoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new ProbeHistoryDbContext(options);
        using var assemblyLoader = new InteractiveAssemblyLoader();

        assemblyLoader.RegisterDependency(typeof(ProbeHistoryDbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(DbContext).Assembly);

        var workspaceAssemblyPaths = new[]
            {
                typeof(ProbeHistoryDbContext).Assembly.Location,
                typeof(DbContext).Assembly.Location
            }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        var session = new ScriptSession(
            typeof(ProbeHistoryDbContext),
            context,
            workspaceAssemblyPaths,
            assemblyLoader);

        await session.EvaluateAsync("var submittedValue = 42;");
        await session.EvaluateProbeAsync("db.Users.Where(user => user.Id > 0)");

        var (source, _, _) = session.CreateCompletionSource("db.", 3);

        Assert.Contains("var submittedValue = 42", source, StringComparison.Ordinal);
        Assert.DoesNotContain("db.Users.Where", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateAsync_unwraps_task_return_value()
    {
        var options = new DbContextOptionsBuilder<ProbeHistoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ProbeHistoryDbContext(options);
        context.Users.AddRange(
            new ProbeHistoryUser { Id = 1 },
            new ProbeHistoryUser { Id = 2 });
        await context.SaveChangesAsync();

        using var assemblyLoader = new InteractiveAssemblyLoader();
        assemblyLoader.RegisterDependency(typeof(ProbeHistoryDbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(DbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(ReplQueryableRuntime).Assembly);

        var workspaceAssemblyPaths = new[]
            {
                typeof(ProbeHistoryDbContext).Assembly.Location,
                typeof(DbContext).Assembly.Location,
                typeof(ReplQueryableRuntime).Assembly.Location
            }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        var session = new ScriptSession(
            typeof(ProbeHistoryDbContext),
            context,
            workspaceAssemblyPaths,
            assemblyLoader,
            preserveAsyncQueries: true);

        var normalized = SnippetNormalizer.ForEvaluation(
            "db.Users.Count();",
            typeof(ProbeHistoryDbContext),
            preserveAsyncQueries: true);

        var result = await session.EvaluateAsync(normalized);

        Assert.Equal(2, result);
    }

}

public sealed class ProbeHistoryDbContext(DbContextOptions<ProbeHistoryDbContext> options)
    : DbContext(options)
{
    public DbSet<ProbeHistoryUser> Users => Set<ProbeHistoryUser>();
}

public sealed class ProbeHistoryUser
{
    public int Id { get; set; }
}
