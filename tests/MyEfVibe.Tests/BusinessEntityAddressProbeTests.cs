namespace MyEfVibe.Tests;

public sealed class BusinessEntityAddressProbeTests
{
    [Fact]
    public void TryCreateProbeExpression_MultiIncludeWithWhere_PreparesTypedTerminalScript()
    {
        const string statement = """
            return await DbContext.BusinessEntityAddresses
                .AsNoTracking()
                .Include(bea => bea.Address)
                .Include(bea => bea.BusinessEntity)
                .Where(bea => bea.BusinessEntityId == businessEntityId)
                .FirstOrDefaultAsync(cancellationToken);
            """;

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(
            statement,
            representativeEntityTypeName: nameof(FakeBusinessEntityAddress),
            dbContextType: typeof(FakeAddressBookDbContext),
            queryEntityTypeName: nameof(FakeBusinessEntityAddress));

        Assert.NotNull(probe);
        Assert.DoesNotContain(".Take(1)", probe, StringComparison.Ordinal);
        Assert.Contains("BusinessEntityId == 0", ProbeTestHelper.CollapseWhitespace(probe), StringComparison.Ordinal);

        var script = SnippetNormalizer.ForEvaluation(
            ProbeScriptFormatter.ToScriptExpression(probe),
            typeof(FakeAddressBookDbContext));

        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime", script, StringComparison.Ordinal);
        Assert.DoesNotContain("businessEntityId", script, StringComparison.Ordinal);
        Assert.DoesNotContain(", object)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreateProbeExpression_DualInclude_cartesian_shape_preserves_include_probe()
    {
        const string statement = """
            return await DbContext.BusinessEntityAddresses
                .AsNoTracking()
                .Include(bea => bea.Address)
                .Include(bea => bea.BusinessEntity)
                .FirstOrDefaultAsync(cancellationToken);
            """;

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(
            statement,
            dbContextType: typeof(FakeAddressBookDbContext));

        Assert.NotNull(probe);
        Assert.DoesNotContain(".Take(1)", probe, StringComparison.Ordinal);
        Assert.Contains(".Include(bea => bea.Address)", probe, StringComparison.Ordinal);
        Assert.Contains(".Include(bea => bea.BusinessEntity)", probe, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreateProbeExpression_FirstOrDefaultAsyncWithPredicate_UsesTypedTerminal()
    {
        const string statement =
            "return await DbContext.BusinessEntityAddresses.FirstOrDefaultAsync(bea => bea.BusinessEntityId == businessEntityId, cancellationToken);";

        var script = SnippetNormalizer.ForEvaluation(statement, typeof(FakeAddressBookDbContext));

        Assert.Contains("FirstOrDefault<", script, StringComparison.Ordinal);
        Assert.Contains("FakeBusinessEntityAddress>", script, StringComparison.Ordinal);
        Assert.Contains("BusinessEntityId == 0", script, StringComparison.Ordinal);
    }
}

public sealed class FakeAddressBookDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public Microsoft.EntityFrameworkCore.DbSet<FakeBusinessEntityAddress> BusinessEntityAddresses =>
        Set<FakeBusinessEntityAddress>();
}

public sealed class FakeBusinessEntityAddress
{
    public int BusinessEntityId { get; set; }

    public FakePostalAddress? Address { get; set; }

    public FakeAddressBookBusinessEntity? BusinessEntity { get; set; }
}

public sealed class FakePostalAddress;

public sealed class FakeAddressBookBusinessEntity;
