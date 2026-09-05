using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class QueryPlanRunnerMixedBatchTests
{
    [Fact]
    public async Task TryExplainAsync_after_execute_does_not_rerun_trailing_insert()
    {
        await using var context = await CreateSqliteContextAsync();
        const string batch =
            "SELECT COUNT(*) AS Total FROM Products; INSERT INTO Products (Name) VALUES ('from-plan');";

        await RawSqlExecutor.ExecuteAsync(context, batch, InspectionAssemblies(), new DbLogSettings());
        Assert.Equal(1, await context.Products.CountAsync());

        var plan = await QueryPlanRunner.TryExplainAsync(context, batch, InspectionAssemblies());

        Assert.False(string.IsNullOrWhiteSpace(plan.PlanText), plan.Note);
        Assert.DoesNotContain("INSERT", plan.PlanText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await context.Products.CountAsync());
    }

    [Fact]
    public async Task TryExplainAsync_does_not_execute_trailing_delete()
    {
        await using var context = await CreateSqliteContextAsync();
        context.Products.Add(new Product { Name = "keep" });
        await context.SaveChangesAsync();

        var plan = await QueryPlanRunner.TryExplainAsync(
            context,
            "SELECT ProductId FROM Products; DELETE FROM Products WHERE ProductId = 1",
            InspectionAssemblies());

        Assert.False(string.IsNullOrWhiteSpace(plan.PlanText), plan.Note);
        Assert.DoesNotContain("DELETE", plan.PlanText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await context.Products.CountAsync());
    }

    private static System.Reflection.Assembly[] InspectionAssemblies()
    {
        return [typeof(RelationalDatabaseFacadeExtensions).Assembly];
    }

    private static async Task<PlanBatchDbContext> CreateSqliteContextAsync()
    {
        var options = new DbContextOptionsBuilder<PlanBatchDbContext>()
            .UseSqlite($"Data Source=plan-batch-{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
            .Options;

        var context = new PlanBatchDbContext(options);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private sealed class Product
    {
        public int ProductId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class PlanBatchDbContext(DbContextOptions<PlanBatchDbContext> options)
        : DbContext(options)
    {
        public DbSet<Product> Products => Set<Product>();
    }
}
