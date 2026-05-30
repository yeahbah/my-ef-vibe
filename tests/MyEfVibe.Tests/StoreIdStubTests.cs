namespace MyEfVibe.Tests;

public sealed class StoreIdStubTests
{
    [Fact]
    public void Stub_StoreIdComparedToBusinessEntityId_UsesNumericZero()
    {
        const string statement = """
                                 return await DbContext.Stores
                                     .AsNoTracking()
                                     .Where(x => x.BusinessEntityId == storeId)
                                     .FirstOrDefaultAsync(cancellationToken);
                                 """;

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(statement);

        Assert.NotNull(probe);
        Assert.Contains("BusinessEntityId == 0", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("storeId", probe, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(probe);
    }

    [Fact]
    public void Stub_StoreIdWithObjectInitializerSelect_StubsStoreId()
    {
        const string statement = """
                                 return await DbContext.Stores
                                     .AsNoTracking()
                                     .Where(x => x.BusinessEntityId == storeId)
                                     .Select(x => new StoreDemographicsProjection
                                     {
                                         BusinessEntityId = x.BusinessEntityId,
                                         Name = x.Name,
                                         Demographics = x.Demographics
                                     })
                                     .FirstOrDefaultAsync(cancellationToken);
                                 """;

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(statement);

        Assert.NotNull(probe);
        Assert.Contains("BusinessEntityId == 0", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("storeId", probe, StringComparison.Ordinal);
        Assert.Contains("StoreDemographicsProjection", probe, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(probe);
    }

    [Fact]
    public void TryCreateProbe_VarStoreAssignment_FirstOrDefaultAsyncWithPredicate()
    {
        const string statement = """
                                 var store = await DbContext.Stores
                                     .FirstOrDefaultAsync(x => x.BusinessEntityId == storeId, cancellationToken)
                                     ?? throw new KeyNotFoundException($"Store with ID {storeId} was not found.");
                                 """;

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(statement);

        Assert.NotNull(probe);
        Assert.Contains("Where(", probe, StringComparison.Ordinal);
        Assert.Contains("BusinessEntityId == 0", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("storeId", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("cancellationToken", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstOrDefaultAsync", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyNotFoundException", probe, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(probe);
    }
}