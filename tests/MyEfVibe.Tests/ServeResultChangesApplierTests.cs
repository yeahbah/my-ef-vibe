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

    [Fact]
    public async Task ApplyAndWriteJsonAsync_uses_model_primary_key_order_for_composite_keys()
    {
        var options = new DbContextOptionsBuilder<ResultGridDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ResultGridDbContext(options);
        context.CompositeProducts.AddRange(
            new CompositeProduct { TenantId = 1, Id = 2, Name = "Target" },
            new CompositeProduct { TenantId = 2, Id = 1, Name = "Wrong row" });
        await context.SaveChangesAsync();

        await ServeResultChangesApplier.ApplyAndWriteJsonAsync(
            CreateRuntime(context),
            "CompositeProducts",
            [
                CompositeChange(
                    tenantId: "1",
                    id: "2",
                    new Dictionary<string, string> { ["Name"] = "Updated" })
            ],
            [],
            CancellationToken.None);

        var target = await context.CompositeProducts.FindAsync([1, 2], CancellationToken.None);
        var wrongRow = await context.CompositeProducts.FindAsync([2, 1], CancellationToken.None);

        Assert.Equal("Updated", target?.Name);
        Assert.Equal("Wrong row", wrongRow?.Name);
    }

    [Fact]
    public async Task ApplyAndWriteJsonAsync_rejects_missing_primary_key_components()
    {
        var options = new DbContextOptionsBuilder<ResultGridDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ResultGridDbContext(options);
        context.CompositeProducts.AddRange(
            new CompositeProduct { TenantId = 0, Id = 2, Name = "Default tenant" },
            new CompositeProduct { TenantId = 1, Id = 2, Name = "Requested tenant" });
        await context.SaveChangesAsync();

        await ServeResultChangesApplier.ApplyAndWriteJsonAsync(
            CreateRuntime(context),
            "CompositeProducts",
            [
                new ServeResultChangesApplier.ServeResultChangeRequest
                {
                    Keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Id"] = "2"
                    },
                    Values = new Dictionary<string, string> { ["Name"] = "Updated" }
                }
            ],
            [],
            CancellationToken.None);

        var defaultTenant = await context.CompositeProducts.FindAsync([0, 2], CancellationToken.None);
        var requestedTenant = await context.CompositeProducts.FindAsync([1, 2], CancellationToken.None);

        Assert.Equal("Default tenant", defaultTenant?.Name);
        Assert.Equal("Requested tenant", requestedTenant?.Name);
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

    private static ServeResultChangesApplier.ServeResultChangeRequest CompositeChange(
        string tenantId,
        string id,
        Dictionary<string, string>? values = null)
    {
        return new ServeResultChangesApplier.ServeResultChangeRequest
        {
            Keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TenantId"] = tenantId,
                ["Id"] = id
            },
            Values = values
        };
    }

    private sealed class ResultGridDbContext(DbContextOptions<ResultGridDbContext> options)
        : DbContext(options)
    {
        public DbSet<ResultGridProduct> Products => Set<ResultGridProduct>();

        public DbSet<CompositeProduct> CompositeProducts => Set<CompositeProduct>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CompositeProduct>()
                .HasKey(static product => new { product.TenantId, product.Id });
        }
    }

    private sealed class ResultGridProduct
    {
        public int Id { get; init; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class CompositeProduct
    {
        public int TenantId { get; init; }

        public int Id { get; init; }

        public string Name { get; set; } = string.Empty;
    }
}
