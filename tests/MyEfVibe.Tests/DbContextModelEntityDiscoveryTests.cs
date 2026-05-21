using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class DbContextModelEntityDiscoveryTests
{
    [Fact]
    public void DiscoverIncludedEntityTypeNames_returns_only_mapped_entities()
    {
        using var context = new MappingProbeContext();

        var direct = context.Model.GetEntityTypes()
            .Select(entityType => entityType.ClrType.Name)
            .ToArray();

        Assert.Contains("MappedProduct", direct);

        var included = DbContextModelEntityDiscovery.DiscoverIncludedEntityTypeNames(context);

        Assert.Contains("MappedProduct", included);
        Assert.DoesNotContain("UnmappedCurrency", included);
    }
}

public sealed class MappedProduct
{
    public int Id { get; set; }
}

public sealed class UnmappedCurrency
{
    public string Code { get; set; } = string.Empty;
}

internal sealed class MappingProbeContext : DbContext
{
    public DbSet<MappedProduct> Products => Set<MappedProduct>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase("mapping-probe");

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Ignore<UnmappedCurrency>();
}
