namespace MyEfVibe.Tests;

public sealed class LinqDeepExpressionAdapterTests
{
    [Fact]
    public void TryCreateProbeExpression_EmployeeIncludeGraph_PreparesTranslatableScript()
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
        Assert.DoesNotContain("AsNoTracking", probe, StringComparison.Ordinal);
        Assert.DoesNotContain(".Take(1)", probe, StringComparison.Ordinal);
        Assert.Contains(".Include(e => e.PersonBusinessEntity)", probe, StringComparison.Ordinal);
        Assert.Contains("BusinessEntityId == 0", ProbeTestHelper.CollapseWhitespace(probe), StringComparison.Ordinal);

        var script = SnippetNormalizer.ForEvaluation(
            ProbeScriptFormatter.ToScriptExpression(probe),
            typeof(FakeAdventureWorksDbContext));

        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime", script, StringComparison.Ordinal);
        Assert.Contains(".Include(", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreateProbeExpression_MultilineIncludeWithLambdas_DoesNotCorruptProbe()
    {
        const string statement = """
            return await DbContext.Employees
                    .Include(e => e.EmployeeDepartmentHistory)
                        .ThenInclude(dh => dh.Department)
                    .Include(e => e.EmployeeDepartmentHistory)
                        .ThenInclude(dh => dh.Shift)
                    .Where(e => e.BusinessEntityId == businessEntityId)
                    .FirstOrDefaultAsync(cancellationToken);
            """;

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(statement);

        Assert.NotNull(probe);
        Assert.DoesNotContain("0>", probe, StringComparison.Ordinal);
        Assert.Contains(".Include(e => e.EmployeeDepartmentHistory)", probe, StringComparison.Ordinal);
        Assert.Contains("BusinessEntityId == 0", ProbeTestHelper.CollapseWhitespace(probe), StringComparison.Ordinal);

        ProbeTestHelper.AssertParsesAsScript(probe);
    }

    [Fact]
    public void TryCreateProbeExpression_DepartmentById_StubsIdAsNumeric()
    {
        const string statement =
            "return await DbContext.Departments.FirstOrDefaultAsync(s => s.DepartmentId == id, cancellationToken);";

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(statement);

        Assert.NotNull(probe);
        Assert.Contains("DepartmentId == 0", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("\"\"", probe, StringComparison.Ordinal);

        ProbeTestHelper.AssertParsesAsScript(probe);
    }

    [Fact]
    public void TryCreateProbeExpression_RewritesDbContextToDb()
    {
        const string statement = "return await DbContext.Products.Take(5).ToListAsync(cancellationToken);";

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(statement);

        Assert.NotNull(probe);
        Assert.StartsWith("db.Products", probe, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreateProbeExpression_VarAssignment_ReturnsQueryableProbe()
    {
        const string statement =
            "var employeeQuery = db.Employees.Where(e => e.JobTitle == jobTitle).AsQueryable();";

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(statement);

        Assert.NotNull(probe);
        Assert.Contains("db.Employees.Where(e => e.JobTitle == \"\")", probe, StringComparison.Ordinal);

        ProbeTestHelper.AssertParsesAsScript(probe);
    }

    [Fact]
    public void TryCreateProbeExpression_EntraObjectIdRowguid_CompilesAsScript()
    {
        const string statement = """
            return await DbContext.BusinessEntities
                .AsNoTracking()
                .Where(be => be.Rowguid == entraObjectId && be.IsEntraUser == true)
                .SelectMany(be => be.Persons)
                .Include(p => p.BusinessEntity)
                .FirstOrDefaultAsync(cancellationToken);
            """;

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(statement);

        Assert.NotNull(probe);
        Assert.Contains("Rowguid == Guid.Empty", ProbeTestHelper.CollapseWhitespace(probe), StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(probe);
    }

    [Fact]
    public void TryCreateProbeExpression_BusinessEntityIdsContains_CompilesAsScript()
    {
        const string statement = """
            return await DbContext.BusinessEntityContacts
                .Include(x => x.ContactType)
                .Include(x => x.Person)
                .ThenInclude(x => x.PersonType)
                .Where(x => businessEntityIds.Contains(x.BusinessEntityId))
                .ToListAsync(cancellationToken);
            """;

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(statement);

        Assert.NotNull(probe);
        Assert.Contains("new int[] { 0 }.Contains(x.BusinessEntityId)", ProbeTestHelper.CollapseWhitespace(probe), StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(probe);
    }

    [Fact]
    public void TryCreateProbeExpression_VarAssignmentWithAnonymousType_CompilesAndStubsParameters()
    {
        const string statement = """
            var raw = await DbContext.ProductReviews
                .AsNoTracking()
                .Where(x => x.ProductId == productId)
                .GroupBy(x => x.Rating)
                .Select(g => new { Rating = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Rating, x => x.Count, cancellationToken);
            """;

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(statement);

        Assert.NotNull(probe);
        Assert.True(
            probe.Contains("ProductId == 0", StringComparison.Ordinal)
                || probe.Contains("ProductId==0", StringComparison.Ordinal),
            $"Probe: {probe}");
        Assert.DoesNotContain("productId", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("cancellationToken", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("ToDictionaryAsync", probe, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(probe);
    }

    [Fact]
    public void TryCreateProbeExpression_SelectUsernameWithNullCoalesce_StripsTerminalAndCoalesce()
    {
        const string statement = """
            var username = await db.Users
                .Where(u => u.Id == note.UserId)
                .Select(u => u.Username)
                .FirstOrDefaultAsync() ?? "Unknown";
            """;

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(
            statement,
            representativeEntityTypeName: "User",
            dbContextType: typeof(FakeGuidNoteDbContext),
            queryEntityTypeName: typeof(FakeGuidUser).FullName);

        Assert.NotNull(probe);
        Assert.DoesNotContain("FirstOrDefaultAsync", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("??", probe, StringComparison.Ordinal);
        Assert.Contains(".Take(1)", probe, StringComparison.Ordinal);
        Assert.Contains("Select", probe, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(ProbeScriptFormatter.ToScriptExpression(probe));
    }

    [Fact]
    public void TryCreateProbeExpression_NonQueryableStatement_ReturnsNull()
    {
        const string statement = "return null;";

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(statement);

        Assert.Null(probe);
    }
}
