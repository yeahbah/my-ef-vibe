using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class QueryEvaluatorMutationFallbackTests
{
    [Theory]
    [InlineData("db.Users.Add(new ProbeHistoryUser());")]
    [InlineData("db.Products.ExecuteUpdate(s => s.SetProperty(p => p.Quantity, p => p.Quantity + 1));")]
    [InlineData("await db.Products.ExecuteDeleteAsync();")]
    [InlineData("db.SaveChanges();")]
    public void ShouldSkipProbe_detects_side_effecting_snippets(string snippet)
    {
        Assert.True(EvaluationSqlFallbackGuard.ShouldSkipProbe(snippet));
    }

    [Theory]
    [InlineData("db.Users.Where(user => user.Id > 0).ToList()")]
    [InlineData("db.Products.Where(p => p.Name == \"ExecuteDelete\").ToList()")]
    [InlineData("db.Products.Select(p => p.CreatedAt.AddDays(1)).ToList()")]
    public void ShouldSkipProbe_allows_read_queries(string snippet)
    {
        Assert.False(EvaluationSqlFallbackGuard.ShouldSkipProbe(snippet));
    }

    [Fact]
    public async Task EvaluateAsync_does_not_re_add_entity_when_recovering_translated_sql()
    {
        await using var context = CreateInMemoryContext();
        using var assemblyLoader = CreateAssemblyLoader();
        var session = new ScriptSession(
            typeof(ProbeHistoryDbContext),
            context,
            CreateWorkspaceAssemblyPaths(),
            assemblyLoader);

        await QueryEvaluator.EvaluateAsync(
            context,
            session,
            "db.Users.Add(new ProbeHistoryUser());",
            new DbLogSettings { Enabled = true },
            [typeof(ProbeHistoryDbContext).Assembly]);

        Assert.Single(context.ChangeTracker.Entries());

        await context.SaveChangesAsync();

        Assert.Equal(1, await context.Users.CountAsync());
    }

    [Fact]
    public async Task EvaluateAsync_still_recovers_translated_sql_for_read_queries_when_dblog_is_off()
    {
        var options = new DbContextOptionsBuilder<MutationFallbackDbContext>()
            .UseSqlite($"Data Source=mutation-fallback-read-{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
            .Options;

        await using var context = new MutationFallbackDbContext(options);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.Products.Add(new MutationFallbackProduct { Name = "Bike", Quantity = 10 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        using var assemblyLoader = CreateAssemblyLoader();
        assemblyLoader.RegisterDependency(typeof(MutationFallbackDbContext).Assembly);
        var workspacePaths = CreateWorkspaceAssemblyPaths()
            .Add(typeof(MutationFallbackDbContext).Assembly.Location);

        var session = new ScriptSession(
            typeof(MutationFallbackDbContext),
            context,
            workspacePaths,
            assemblyLoader);

        var (_, metrics) = await QueryEvaluator.EvaluateAsync(
            context,
            session,
            "db.Products.Where(p => p.Quantity > 0).ToList()",
            new DbLogSettings { Enabled = false },
            [
                typeof(MutationFallbackDbContext).Assembly,
                typeof(RelationalDatabaseFacadeExtensions).Assembly
            ]);

        Assert.False(string.IsNullOrWhiteSpace(metrics.TranslatedSql));
        Assert.Equal(1, await context.Products.CountAsync());
    }

    [Fact]
    public async Task EvaluateAsync_does_not_apply_execute_update_twice_when_dblog_is_off()
    {
        var options = new DbContextOptionsBuilder<MutationFallbackDbContext>()
            .UseSqlite($"Data Source=mutation-fallback-{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
            .Options;

        await using var context = new MutationFallbackDbContext(options);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.Products.Add(new MutationFallbackProduct { Name = "Bike", Quantity = 10 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        using var assemblyLoader = CreateAssemblyLoader();
        assemblyLoader.RegisterDependency(typeof(MutationFallbackDbContext).Assembly);
        var workspacePaths = CreateWorkspaceAssemblyPaths()
            .Add(typeof(MutationFallbackDbContext).Assembly.Location);

        var session = new ScriptSession(
            typeof(MutationFallbackDbContext),
            context,
            workspacePaths,
            assemblyLoader);

        await QueryEvaluator.EvaluateAsync(
            context,
            session,
            "db.Products.ExecuteUpdate(s => s.SetProperty(p => p.Quantity, p => p.Quantity + 1));",
            new DbLogSettings { Enabled = false },
            [
                typeof(MutationFallbackDbContext).Assembly,
                typeof(RelationalDatabaseFacadeExtensions).Assembly
            ]);

        Assert.Equal(11, await context.Products.Select(product => product.Quantity).SingleAsync());
    }

    private static ProbeHistoryDbContext CreateInMemoryContext()
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

public sealed class MutationFallbackProduct
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Quantity { get; set; }
}

public sealed class MutationFallbackDbContext(DbContextOptions<MutationFallbackDbContext> options)
    : DbContext(options)
{
    public DbSet<MutationFallbackProduct> Products => Set<MutationFallbackProduct>();
}
