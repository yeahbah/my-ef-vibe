using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class RawSqlExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_returns_rows_for_batch_after_non_query_statement()
    {
        var options = new DbContextOptionsBuilder<RawSqlTestContext>()
            .UseSqlite($"Data Source=raw-sql-{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
            .Options;

        await using var context = new RawSqlTestContext(options);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();

        var (result, metrics, rows) = await RawSqlExecutor.ExecuteAsync(
            context,
            "INSERT INTO Products (Name) VALUES ('first'); SELECT COUNT(*) AS Total FROM Products;",
            [typeof(RelationalDatabaseFacadeExtensions).Assembly],
            new DbLogSettings());

        Assert.Equal("1", result);
        Assert.NotNull(rows);
        var row = Assert.Single(rows);
        Assert.Equal("1", row["Total"]);
        Assert.Equal(1, await context.Products.CountAsync());
        Assert.Equal(ResultKind.Enumerable, metrics.ResultKind);
    }

    private sealed class Product
    {
        public int ProductId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class RawSqlTestContext(DbContextOptions<RawSqlTestContext> options)
        : DbContext(options)
    {
        public DbSet<Product> Products => Set<Product>();
    }
}
