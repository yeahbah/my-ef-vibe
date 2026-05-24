namespace MyEfVibe.Tests;

public sealed class ProbeTranslationPipelineTests
{
    [Theory]
    [InlineData("db.Orders.Include(o => o.Lines).ToListAsync()")]
    [InlineData("db.Orders.ThenInclude(o => o.Customer)")]
    [InlineData("return db.Products.Include(p => p.Category).Where(p => p.Id == 1);")]
    public void ContainsEagerLoad_detects_include_operators(string expression)
    {
        Assert.True(SqlTranslationProbe.ContainsEagerLoad(expression));
    }

    [Theory]
    [InlineData("db.Orders.Where(o => o.Total > 0).ToListAsync()")]
    [InlineData("db.Products.Take(10)")]
    public void ContainsEagerLoad_returns_false_without_include(string expression)
    {
        Assert.False(SqlTranslationProbe.ContainsEagerLoad(expression));
    }

    [Fact]
    public void TryExtractPredicateArgument_extracts_lambda_from_mixed_arguments()
    {
        const string arguments = "bea => bea.BusinessEntityId == 0, cancellationToken";

        var predicate = SqlTranslationProbe.TryExtractPredicateArgument(arguments);

        Assert.Equal("bea => bea.BusinessEntityId == 0", predicate);
    }

    [Fact]
    public void TryExtractPredicateArgument_returns_null_for_cancellation_token_only()
    {
        const string arguments = "cancellationToken";

        Assert.Null(SqlTranslationProbe.TryExtractPredicateArgument(arguments));
    }

    [Fact]
    public void ProbeScriptFormatter_strips_as_no_tracking_from_include_probe()
    {
        const string probe = """
            db.BusinessEntityAddresses
                .AsNoTracking()
                .Include(bea => bea.Address)
                .Where(bea => bea.BusinessEntityId == 0)
            """;

        var formatted = ProbeScriptFormatter.ToScriptExpression(probe);

        Assert.DoesNotContain("AsNoTracking", formatted, StringComparison.Ordinal);
        Assert.Contains(".Include(bea => bea.Address)", formatted, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(
            $"_ = global::MyEfVibe.ReplQueryableRuntime.Where<global::MyEfVibe.Tests.FakeBusinessEntityAddress>({formatted}, bea => bea.BusinessEntityId == 0);");
    }

    [Fact]
    public void FullPipeline_employee_include_graph_matches_repository_snippet_shape()
    {
        const string statement = """
            return await DbContext.Employees
                .AsNoTracking()
                .Include(e => e.PersonBusinessEntity)
                .Include(e => e.EmployeeDepartmentHistory)
                    .ThenInclude(dh => dh.Department)
                .Include(e => e.EmployeeDepartmentHistory)
                    .ThenInclude(dh => dh.Shift)
                .Include(e => e.EmployeePayHistory)
                .Where(e => e.BusinessEntityId == businessEntityId)
                .FirstOrDefaultAsync(cancellationToken);
            """;

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(
            statement,
            representativeEntityTypeName: nameof(FakeEmployee),
            dbContextType: typeof(FakeAdventureWorksDbContext),
            queryEntityTypeName: nameof(FakeEmployee));

        Assert.NotNull(probe);
        Assert.DoesNotContain(".Take(1)", probe, StringComparison.Ordinal);
        Assert.Contains("ThenInclude(dh => dh.Department)", probe, StringComparison.Ordinal);
        Assert.Contains("BusinessEntityId == 0", ProbeTestHelper.CollapseWhitespace(probe), StringComparison.Ordinal);

        var script = SnippetNormalizer.ForEvaluation(
            ProbeScriptFormatter.ToScriptExpression(probe),
            typeof(FakeAdventureWorksDbContext));

        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime", script, StringComparison.Ordinal);
        Assert.Contains("Where<", script, StringComparison.Ordinal);
        Assert.Contains("FakeEmployee>", script, StringComparison.Ordinal);
        Assert.Contains(".Include(", script, StringComparison.Ordinal);
        Assert.Contains("ThenInclude(dh => dh.Department)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("businessEntityId", script, StringComparison.Ordinal);
    }

    [Fact]
    public void FullPipeline_odata_context_where_key_produces_typed_where_script()
    {
        const string statement = "var entity = _context.Cities.Where(x => x.CityId == key);";

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(
            statement,
            representativeEntityTypeName: nameof(FakeODataCity),
            dbContextType: typeof(FakeODataDbContext),
            queryEntityTypeName: nameof(FakeODataCity));

        Assert.NotNull(probe);
        Assert.Contains("db.Cities.Where(x => x.CityId == 0)", ProbeTestHelper.CollapseWhitespace(probe), StringComparison.Ordinal);

        var script = SnippetNormalizer.ForEvaluation(
            ProbeScriptFormatter.ToScriptExpression(probe),
            typeof(FakeODataDbContext));

        Assert.Contains("ReplQueryableRuntime.Where<", script, StringComparison.Ordinal);
        Assert.Contains("FakeODataCity>", script, StringComparison.Ordinal);
        Assert.Contains("CityId == 0", script, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(script);
    }

    [Fact]
    public void RepositorySnippetAdapter_include_probe_uses_typed_where_when_chain_ends_with_where()
    {
        const string probe =
            "db.BusinessEntityAddresses.Include(bea => bea.Address).Where(bea => bea.BusinessEntityId == 0)";

        var script = RepositorySnippetAdapter.PrepareForEvaluation(probe, typeof(FakeAddressBookDbContext));

        Assert.Contains("ReplQueryableRuntime.Where<", script, StringComparison.Ordinal);
        Assert.Contains("FakeBusinessEntityAddress>", script, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(script);
    }

    [Fact]
    public void SnippetNormalizer_routes_collapsed_include_probe_through_repository_adapter()
    {
        const string collapsed =
            "db.Employees.Include(e => e.Department).Where(e => e.BusinessEntityId == 0).FirstOrDefaultAsync(cancellationToken)";

        var normalized = SnippetNormalizer.ForEvaluation(collapsed, typeof(FakeAdventureWorksDbContext));

        Assert.Contains("ReplQueryableRuntime", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstOrDefaultAsync", normalized, StringComparison.Ordinal);
    }
}

public sealed class FakeODataDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public Microsoft.EntityFrameworkCore.DbSet<FakeODataCity> Cities => Set<FakeODataCity>();
}

public sealed class FakeODataCity
{
    public int CityId { get; set; }
}
