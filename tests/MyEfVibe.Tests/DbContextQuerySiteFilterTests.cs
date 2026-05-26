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
    public void BelongsToSelectedContext_IncludesInjectedContextField()
    {
        var scope = CreateScope("WideWorldImportersContext");

        Assert.True(DbContextQuerySiteFilter.BelongsToSelectedContext(
            "var entity = _context.Cities.Where(x => x.CityId == key);",
            scope,
            containingTypeName: "EntitiesController",
            containingTypeIndex: null));
    }

    [Fact]
    public void BelongsToSelectedContext_IncludesMediatrHandlerBoundToSelectedContextInterface()
    {
        const string source = """
            public class GetProductsListQueryHandler : IRequestHandler<GetProductsListQuery, ProductsListVm>
            {
                private readonly INorthwindDbContext _context;

                public GetProductsListQueryHandler(INorthwindDbContext context)
                {
                    _context = context;
                }
            }
            """;

        var scope = new DbContextScanScope(
            "NorthwindDbContext",
            new HashSet<string>(["NorthwindDbContext", "INorthwindDbContext"], StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));
        var index = DbContextContainingTypeIndex.Build(source, scope);

        Assert.True(DbContextQuerySiteFilter.BelongsToSelectedContext(
            "var products = await _context.Products.OrderBy(p => p.ProductName).ToListAsync(cancellationToken);",
            scope,
            containingTypeName: "GetProductsListQueryHandler",
            index));
    }

    [Fact]
    public void BelongsToSelectedContext_ExcludesHandlerBoundToOtherContextInterface()
    {
        const string source = """
            public class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, UsersVm>
            {
                private readonly IUserSetupDbContext _context;
            }
            """;

        var scope = new DbContextScanScope(
            "NorthwindDbContext",
            new HashSet<string>(["NorthwindDbContext", "INorthwindDbContext"], StringComparer.Ordinal),
            new HashSet<string>(["UserSetupDbContext", "IUserSetupDbContext"], StringComparer.Ordinal));
        var index = DbContextContainingTypeIndex.Build(source, scope);

        Assert.False(DbContextQuerySiteFilter.BelongsToSelectedContext(
            "var users = await _context.Users.ToListAsync(cancellationToken);",
            scope,
            containingTypeName: "GetUsersQueryHandler",
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
