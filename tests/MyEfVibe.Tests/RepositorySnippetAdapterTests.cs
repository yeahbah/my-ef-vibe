namespace MyEfVibe.Tests;

public sealed class RepositorySnippetAdapterTests
{
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
    public void ReplQueryableRuntime_IsPublicForRoslynScriptSubmissions()
    {
        var type = typeof(ReplQueryableRuntime);

        Assert.True(type.IsPublic);
        Assert.True(type.GetMethod(nameof(ReplQueryableRuntime.FirstOrDefault), [typeof(object)])!.IsPublic);
    }
}

public sealed class FakeAdventureWorksDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public Microsoft.EntityFrameworkCore.DbSet<FakeBusinessEntity> BusinessEntities => Set<FakeBusinessEntity>();

    public Microsoft.EntityFrameworkCore.DbSet<FakeEmployee> Employees => Set<FakeEmployee>();
}

public sealed class FakeEmployee
{
    public int BusinessEntityId { get; set; }

    public FakePersonBusinessEntity? PersonBusinessEntity { get; set; }
}

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
