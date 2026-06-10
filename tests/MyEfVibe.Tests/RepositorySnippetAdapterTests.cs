using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class RepositorySnippetAdapterTests
{
    [Fact]
    public void PrepareForEvaluation_SyncCount_UsesAsyncRuntimeWhenPreserveAsync()
    {
        const string snippet = "db.BusinessEntities.Count();";

        var normalized = SnippetNormalizer.ForEvaluation(
            snippet,
            typeof(FakeAdventureWorksDbContext),
            preserveAsyncQueries: true);

        Assert.Contains("await ", normalized, StringComparison.Ordinal);
        Assert.Contains("CountAsync", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("ReplQueryableRuntime.Count(", normalized, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(normalized);
    }

    [Fact]
    public void PrepareForEvaluation_PreservesAsyncWhenRequested()
    {
        const string snippet = """
                               await db.Products
                                   .Where(p => p.ListPrice > 0)
                                   .Take(10)
                                   .ToListAsync();
                               """;

        var normalized = SnippetNormalizer.ForEvaluation(
            snippet,
            typeof(FakeAdventureWorksDbContext),
            preserveAsyncQueries: true);

        Assert.Contains("ToListAsync", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("ToList(", normalized, StringComparison.Ordinal);
        Assert.Contains("db.Products", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareForEvaluation_RepositoryQuery_StripsAwaitDbContextAndParameters()
    {
        const string snippet = """
                               await DbContext.BusinessEntities
                                   .AsNoTracking()
                                   .Where(be => be.Rowguid == entraObjectId && be.IsEntraUser == true)
                                   .SelectMany(be => be.Persons)
                                   .Include(p => p.BusinessEntity)
                                   .Include(p => p.PersonType)
                                   .Include(p => p.EmailAddresses)
                                   .FirstOrDefaultAsync(cancellationToken);
                               """;

        var normalized = SnippetNormalizer.ForEvaluation(snippet, typeof(FakeAdventureWorksDbContext));

        Assert.DoesNotContain("await ", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("entraObjectId", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("cancellationToken", normalized, StringComparison.Ordinal);
        Assert.Contains("db.BusinessEntities", normalized, StringComparison.Ordinal);
        Assert.Contains("Rowguid == Guid.Empty", normalized, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefault", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstOrDefaultAsync", normalized, StringComparison.Ordinal);
        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime", normalized, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(normalized);
    }

    [Fact]
    public void TryRewriteBareWhere_IncludeChain_UsesTypedWhere()
    {
        const string probe =
            "db.Employees.Include(e => e.PersonBusinessEntity).Where(e => e.BusinessEntityId == 0)";

        var rewritten = EfReplQueryableRewriter.TryRewriteBareWhere(probe, typeof(FakeAdventureWorksDbContext));

        Assert.NotNull(rewritten);
        Assert.Contains("ReplQueryableRuntime.Where<", rewritten, StringComparison.Ordinal);
        Assert.Contains("FakeEmployee>", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareForEvaluation_EmployeeIncludeQuery_RewritesToPublicRuntime()
    {
        const string snippet = """
                               DbContext.Employees
                                   .Include(e => e.PersonBusinessEntity)
                                       .ThenInclude(p => p.EmailAddresses)
                                   .Where(e => e.BusinessEntityId == businessEntityId)
                                   .FirstOrDefaultAsync(cancellationToken);
                               """;

        var normalized = SnippetNormalizer.ForEvaluation(snippet, typeof(FakeAdventureWorksDbContext));

        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.FirstOrDefault", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", normalized, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(normalized);
    }

    [Fact]
    public void PrepareForEvaluation_AnonymousTypeWithMultipleAwaits_StripsAwaitAfterSyncRewrite()
    {
        const string snippet = """
                               var stats = new
                               {
                                   Films = await db.Films.CountAsync(cancellationToken),
                                   Rentals = await db.Rentals.CountAsync(cancellationToken),
                                   Customers = await db.Customers.CountAsync(cancellationToken),
                                   Actors = await db.Actors.CountAsync(cancellationToken)
                               };
                               """;

        var normalized = SnippetNormalizer.ForEvaluation(snippet, typeof(FakePagilaDbContext));

        Assert.DoesNotContain("await ", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("CountAsync", normalized, StringComparison.Ordinal);
        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.Count(db.Films)", normalized, StringComparison.Ordinal);
        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.Count(db.Rentals)", normalized, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(normalized);
    }

    [Fact]
    public void ReplQueryableRuntime_IsPublicForRoslynScriptSubmissions()
    {
        var type = typeof(ReplQueryableRuntime);

        Assert.True(type.IsPublic);
        Assert.True(type.GetMethod(nameof(ReplQueryableRuntime.FirstOrDefault), [typeof(object)])!.IsPublic);
    }
}

public sealed class FakeAdventureWorksDbContext : DbContext
{
    public DbSet<FakeBusinessEntity> BusinessEntities => Set<FakeBusinessEntity>();

    public DbSet<FakeEmployee> Employees => Set<FakeEmployee>();
}

public sealed class FakePagilaDbContext : DbContext
{
    public DbSet<FakeFilm> Films => Set<FakeFilm>();

    public DbSet<FakeRental> Rentals => Set<FakeRental>();

    public DbSet<FakeCustomer> Customers => Set<FakeCustomer>();

    public DbSet<FakeActor> Actors => Set<FakeActor>();
}

public sealed class FakeFilm;

public sealed class FakeRental;

public sealed class FakeCustomer;

public sealed class FakeActor;

public sealed class FakeEmployee
{
    public int BusinessEntityId { get; set; }

    public FakePersonBusinessEntity? PersonBusinessEntity { get; set; }

    public ICollection<FakeEmployeeDepartmentHistory> EmployeeDepartmentHistory { get; set; } = [];

    public ICollection<FakeEmployeePayHistory> EmployeePayHistory { get; set; } = [];
}

public sealed class FakeEmployeeDepartmentHistory
{
    public FakeDepartment? Department { get; set; }

    public FakeShift? Shift { get; set; }
}

public sealed class FakeDepartment;

public sealed class FakeShift;

public sealed class FakeEmployeePayHistory;

public sealed class FakePersonBusinessEntity
{
    public ICollection<FakeEmailAddress> EmailAddresses { get; set; } = [];
}

public sealed class FakeEmailAddress
{
}

public sealed class FakeBusinessEntity
{
    public Guid Rowguid { get; set; }

    public bool IsEntraUser { get; set; }

    public ICollection<FakePerson> Persons { get; set; } = [];
}

public sealed class FakePerson
{
    public FakeBusinessEntity BusinessEntity { get; set; } = null!;

    public object? PersonType { get; set; }

    public ICollection<object> EmailAddresses { get; set; } = [];
}