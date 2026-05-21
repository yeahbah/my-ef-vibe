namespace MyEfVibe.Tests;

public sealed class DbContextQuerySiteFilterTests
{
    private static DbContextScanScope CreateScope(string selected, params string[] others) =>
        new(selected, others.ToHashSet(StringComparer.Ordinal));

    [Fact]
    public void BelongsToSelectedContext_NullScope_IncludesAll()
    {
        Assert.True(DbContextQuerySiteFilter.BelongsToSelectedContext(
            "await db.Products.ToListAsync();",
            scope: null,
            containingTypeName: null,
            containingTypeIndex: null));
    }

    [Fact]
    public void BelongsToSelectedContext_ScopeWithSingleDiscoveredContext_StillFilters()
    {
        var scope = CreateScope("AdventureWorksDbContext");
        var index = new DbContextContainingTypeIndex();

        Assert.False(DbContextQuerySiteFilter.BelongsToSelectedContext(
            "await AdventureWorksUserSetupContext.Users.ToListAsync();",
            scope,
            containingTypeName: null,
            index));

        Assert.True(DbContextQuerySiteFilter.BelongsToSelectedContext(
            "await AdventureWorksDbContext.Products.ToListAsync();",
            scope,
            containingTypeName: null,
            index));
    }

    [Fact]
    public void BelongsToSelectedContext_ExcludesExplicitOtherContext()
    {
        var scope = CreateScope("AdventureWorksDbContext", "AdventureWorksUserSetupContext");
        var index = new DbContextContainingTypeIndex();

        Assert.False(DbContextQuerySiteFilter.BelongsToSelectedContext(
            "await AdventureWorksUserSetupContext.Users.ToListAsync();",
            scope,
            containingTypeName: null,
            index));
    }

    [Fact]
    public void BelongsToSelectedContext_RepositoryBoundToOtherContext_ExcludesEvenWithDbAlias()
    {
        const string source = """
            public sealed class UserSetupRepository(AdventureWorksUserSetupContext dbContext)
                : EfRepository<UserEntity>(dbContext)
            {
            }
            """;

        var scope = CreateScope("AdventureWorksDbContext", "AdventureWorksUserSetupContext");
        var index = DbContextContainingTypeIndex.Build(source, scope);

        Assert.False(DbContextQuerySiteFilter.BelongsToSelectedContext(
            "await db.Users.ToListAsync();",
            scope,
            containingTypeName: "UserSetupRepository",
            index));
    }

    [Fact]
    public void BelongsToSelectedContext_RepositoryBoundToSelected_IncludesDbContextAlias()
    {
        const string source = """
            public sealed class EmployeeRepository(AdventureWorksDbContext dbContext)
                : EfRepository<EmployeeEntity>(dbContext)
            {
            }
            """;

        var scope = CreateScope("AdventureWorksDbContext", "AdventureWorksUserSetupContext");
        var index = DbContextContainingTypeIndex.Build(source, scope);

        Assert.True(DbContextQuerySiteFilter.BelongsToSelectedContext(
            """
            var addresses = await DbContext.BusinessEntityAddresses
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            """,
            scope,
            containingTypeName: "EmployeeRepository",
            index));
    }
}
