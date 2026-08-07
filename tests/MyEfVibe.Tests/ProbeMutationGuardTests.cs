using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.EntityFrameworkCore;
using MyEfVibe.Linq;

namespace MyEfVibe.Tests;

public sealed class ProbeMutationGuardTests
{
    [Theory]
    [InlineData("db.Products.Where(p => p.ProductId > 0).ExecuteDelete()")]
    [InlineData("await db.Products.Where(p => p.ProductId > 0).ExecuteDeleteAsync();")]
    [InlineData("EntityFrameworkQueryableExtensions.ExecuteDeleteAsync<Product>(db.Products)")]
    [InlineData("db.Products.Where(p => p.ListPrice < 1).ExecuteUpdate(s => s.SetProperty(p => p.ListPrice, 0));")]
    [InlineData("db.Database.ExecuteSqlRaw(\"DELETE FROM Production.Product\");")]
    [InlineData("await _dbContext.Database.ExecuteSqlRawAsync(\"DELETE FROM Production.Product\");")]
    [InlineData("db.Database.ExecuteSqlInterpolated($\"DELETE FROM Production.Product WHERE ProductID = {id}\");")]
    [InlineData("db.SaveChanges()")]
    [InlineData("await db.Database.EnsureDeletedAsync();")]
    [InlineData("db.Database.GetService<IRelationalDatabaseCreator>().EnsureDeleted()")]
    [InlineData("db.GetService<IRelationalDatabaseCreator>().Delete()")]
    public void ContainsMutatingOperation_detects_eager_mutators(string expression)
    {
        Assert.True(ProbeMutationGuard.ContainsMutatingOperation(expression));
    }

    [Theory]
    [InlineData("db.Products.Where(p => p.ProductId > 0).ToList()")]
    [InlineData("db.Products.Count()")]
    [InlineData("db.Products.Where(p => p.Name == \"ExecuteDelete\").ToList()")]
    public void ContainsMutatingOperation_allows_read_queries(string expression)
    {
        Assert.False(ProbeMutationGuard.ContainsMutatingOperation(expression));
    }

    [Theory]
    [InlineData("db.Products.Where(p => p.ProductId > 0).ExecuteDelete()")]
    [InlineData("db.Database.ExecuteSqlRaw(\"DELETE FROM Production.Product\")")]
    [InlineData("await db.Products.ExecuteDeleteAsync();")]
    public void TryCreateProbeExpression_rejects_mutating_terminals(string expression)
    {
        Assert.Null(SqlTranslationProbe.TryCreateProbeExpression(expression));
    }

    [Fact]
    public void LinqDeepExpressionAdapter_rejects_ExecuteDelete_statement_collected_via_Where()
    {
        const string statement = """
                                 await db.Products
                                     .Where(p => p.ProductId > 0)
                                     .ExecuteDeleteAsync();
                                 """;

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(
            statement,
            nameof(QueryCompProduct),
            typeof(QueryCompProductDbContext),
            nameof(QueryCompProduct));

        Assert.Null(probe);
    }

    [Fact]
    public async Task EvaluateProbeAsync_ExecuteDelete_would_mutate_live_database()
    {
        // Documents why mutating probes must never reach EvaluateProbeAsync during deep scan / translate.
        var options = new DbContextOptionsBuilder<MutationProbeDbContext>()
            .UseSqlite($"Data Source=probe-mutation-{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
            .Options;

        await using var context = new MutationProbeDbContext(options);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.Products.Add(new MutationProbeProduct { Name = "Bike" });
        await context.SaveChangesAsync();

        using var assemblyLoader = new InteractiveAssemblyLoader();
        assemblyLoader.RegisterDependency(typeof(MutationProbeDbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(DbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(ReplQueryableRuntime).Assembly);

        var workspaceAssemblyPaths = new[]
            {
                typeof(MutationProbeDbContext).Assembly.Location,
                typeof(DbContext).Assembly.Location,
                typeof(ReplQueryableRuntime).Assembly.Location,
            }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        var session = new ScriptSession(
            typeof(MutationProbeDbContext),
            context,
            workspaceAssemblyPaths,
            assemblyLoader);

        await session.InitializeAsync(Path.GetTempPath());

        Assert.Equal(1, await context.Products.CountAsync());

        await session.EvaluateProbeAsync("db.Products.Where(p => p.Id > 0).ExecuteDelete()");

        Assert.Equal(0, await context.Products.CountAsync());
    }
}

public sealed class MutationProbeProduct
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class MutationProbeDbContext(DbContextOptions<MutationProbeDbContext> options)
    : DbContext(options)
{
    public DbSet<MutationProbeProduct> Products => Set<MutationProbeProduct>();
}
