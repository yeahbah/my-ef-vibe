using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class DbSetPropertyEntityExtractorTests
{
    [Fact]
    public void TryExtractConcreteEntityTypeName_finds_entity_from_DbContext_property()
    {
        const string code = """
            var addresses = await DbContext.BusinessEntityAddresses
                .AsNoTracking()
                .Where(bea => bea.BusinessEntityId == businessEntityId)
                .ToListAsync(cancellationToken);
            """;

        Assert.True(
            DbSetPropertyEntityExtractor.TryExtractConcreteEntityTypeName(
                code,
                typeof(MappingProbeContextWithDbSet),
                out var entityType));

        Assert.Equal(nameof(UnmappedCurrency), entityType);
    }

    [Fact]
    public void QueryableEntityTypeResolver_prefers_Set_over_DbSet_property()
    {
        const string code = "return await DbContext.Set<MappedProduct>().ToListAsync();";

        Assert.True(
            QueryableEntityTypeResolver.TryExtractConcreteEntityTypeName(
                code,
                typeof(MappingProbeContext),
                out var entityType));

        Assert.Equal(nameof(MappedProduct), entityType);
    }

    [Fact]
    public void Unmapped_DbSet_property_is_detected_for_sqlite_style_model()
    {
        using var context = new MappingProbeContextWithDbSet();
        const string code = "await DbContext.BusinessEntityAddresses.ToListAsync();";

        Assert.True(
            QueryableEntityTypeResolver.TryExtractConcreteEntityTypeName(
                code,
                context.GetType(),
                out var entityType));

        var included = DbContextModelEntityDiscovery.DiscoverIncludedEntityTypeNames(context);

        Assert.Equal(nameof(UnmappedCurrency), entityType);
        Assert.DoesNotContain(entityType, included);
    }
}

internal sealed class MappingProbeContextWithDbSet : DbContext
{
    public DbSet<UnmappedCurrency> BusinessEntityAddresses => Set<UnmappedCurrency>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseInMemoryDatabase("dbset-property-probe");

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Ignore<UnmappedCurrency>();
}
