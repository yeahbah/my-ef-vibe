namespace MyEfVibe.Tests;

using Microsoft.EntityFrameworkCore;
using MyEfVibe.Workspace;

public sealed class ServeResultChangesApplierTests
{
    [Fact]
    public async Task ApplyAndWriteJsonAsync_clears_tracked_changes_after_failed_batch()
    {
        var options = new DbContextOptionsBuilder<ResultGridDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ResultGridDbContext(options);
        context.Products.AddRange(
            new ResultGridProduct { Id = 1, Name = "Seed" },
            new ResultGridProduct { Id = 2, Name = "Original" });
        await context.SaveChangesAsync();

        var runtime = CreateRuntime(context);

        await ServeResultChangesApplier.ApplyAndWriteJsonAsync(
            runtime,
            "Products",
            [],
            [Change("1"), Change("999")],
            CancellationToken.None);

        await ServeResultChangesApplier.ApplyAndWriteJsonAsync(
            runtime,
            "Products",
            [Change("2", new Dictionary<string, string> { ["Name"] = "Updated" })],
            [],
            CancellationToken.None);

        var products = await context.Products
            .AsNoTracking()
            .OrderBy(static product => product.Id)
            .ToListAsync();

        Assert.Equal([1, 2], products.Select(static product => product.Id).ToArray());
        Assert.Equal("Seed", products[0].Name);
        Assert.Equal("Updated", products[1].Name);
    }

    private static WorkspaceRuntime CreateRuntime(object dbContext)
    {
        return new WorkspaceRuntime(
            null!,
            null!,
            dbContext,
            new DbLogSettings(),
            string.Empty,
            string.Empty,
            dbContext.GetType().Name);
    }

    private static ServeResultChangesApplier.ServeResultChangeRequest Change(
        string id,
        Dictionary<string, string>? values = null)
    {
        return new ServeResultChangesApplier.ServeResultChangeRequest
        {
            Keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Id"] = id
            },
            Values = values
        };
    }

    private sealed class ResultGridDbContext(DbContextOptions<ResultGridDbContext> options)
        : DbContext(options)
    {
        public DbSet<ResultGridProduct> Products => Set<ResultGridProduct>();
    }

    private sealed class ResultGridProduct
    {
        public int Id { get; init; }

        public string Name { get; set; } = string.Empty;
    }
}
