using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class ProbeParameterStubberTests
{
    [Fact]
    public void Stub_ODataKeyParameter_UsesNumericZero()
    {
        const string probe = "db.Cities.Where(x => x.CityId == key)";

        var stubbed = ProbeParameterStubber.Stub(probe);

        Assert.Contains("CityId == 0", stubbed, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(stubbed);
    }

    [Fact]
    public void Stub_IdComparedToDepartmentId_UsesNumericZero()
    {
        const string probe = "db.Departments.Where(s => s.DepartmentId == id).Take(1)";

        var stubbed = ProbeParameterStubber.Stub(probe);

        Assert.Contains("DepartmentId == 0", stubbed, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(stubbed);
    }

    [Fact]
    public void Stub_Code_UsesEmptyString()
    {
        const string probe = "db.Products.Where(p => p.ProductCode == code)";

        var stubbed = ProbeParameterStubber.Stub(probe);

        Assert.Contains("ProductCode == \"\"", stubbed, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(stubbed);
    }

    [Fact]
    public void Stub_BusinessEntityId_UsesNumericZero()
    {
        const string probe = "db.Employees.Where(e => e.BusinessEntityId == businessEntityId)";

        var stubbed = ProbeParameterStubber.Stub(probe);

        Assert.Contains("BusinessEntityId == 0", stubbed, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(stubbed);
    }

    [Fact]
    public void Stub_CancellationToken_NotReplacedWithTrueLiteral()
    {
        const string probe =
            "db.Stores.FirstOrDefaultAsync(x => x.BusinessEntityId == storeId, cancellationToken)";

        var stubbed = ProbeParameterStubber.Stub(probe);

        Assert.DoesNotContain(", true)", stubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("cancellationToken", stubbed, StringComparison.Ordinal);
        Assert.Contains("BusinessEntityId == 0", stubbed, StringComparison.Ordinal);
    }

    [Fact]
    public void Stub_CancellationToken_RemovedFromTerminalCall()
    {
        const string probe = "db.Departments.FirstOrDefaultAsync(cancellationToken)";

        var stubbed = ProbeParameterStubber.Stub(probe);

        Assert.DoesNotContain("cancellationToken", stubbed, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(stubbed);
    }

    [Fact]
    public void Stub_LambdaParameter_NotReplaced()
    {
        const string probe = "db.Employees.Where(e => e.JobTitle == jobTitle)";

        var stubbed = ProbeParameterStubber.Stub(probe);

        Assert.Contains("e.JobTitle", stubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("0.JobTitle", stubbed, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(stubbed);
    }

    [Fact]
    public void Stub_IsActive_UsesTrueLiteral()
    {
        const string probe = "db.Employees.Where(e => e.Active == isActive)";

        var stubbed = ProbeParameterStubber.Stub(probe);

        Assert.Contains("== true", stubbed, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(stubbed);
    }

    [Fact]
    public void Stub_EntraObjectIdComparedToRowguid_UsesGuidEmpty()
    {
        const string probe =
            "db.BusinessEntities.Where(be => be.Rowguid == entraObjectId && be.IsEntraUser == true)";

        var stubbed = ProbeParameterStubber.Stub(probe);

        Assert.Contains("Rowguid == Guid.Empty", stubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("Rowguid == 0", stubbed, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(stubbed);
    }

    [Fact]
    public void Stub_GuidEntityIdComparedToRouteId_UsesGuidEmpty()
    {
        const string probe = "db.Notes.Where(n => n.Id == id)";

        var stubbed = ProbeParameterStubber.Stub(
            probe,
            new ProbeStubContext(typeof(FakeGuidNoteDbContext), typeof(FakeGuidNote).FullName!));

        Assert.Contains("Id == Guid.Empty", stubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("Id == 0", stubbed, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(stubbed);
    }

    [Fact]
    public void Stub_BusinessEntityIdsContains_UsesIntArrayLiteral()
    {
        const string probe =
            "db.BusinessEntityContacts.Where(x => businessEntityIds.Contains(x.BusinessEntityId))";

        var stubbed = ProbeParameterStubber.Stub(probe);

        Assert.Contains("new int[] { 0 }.Contains(x.BusinessEntityId)", stubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("businessEntityIds", stubbed, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(stubbed);
    }

    [Fact]
    public void Stub_MethodCallRoot_IsNotReplaced()
    {
        const string probe = "ActiveProducts().OrderBy(p => p.Name).Take(DefaultTake)";

        var stubbed = ProbeParameterStubber.Stub(probe);

        Assert.StartsWith("ActiveProducts()", stubbed, StringComparison.Ordinal);
        Assert.Contains(".Take(0)", stubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("0()", stubbed, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(stubbed);
    }

    [Fact]
    public void Stub_QueryComprehensionRangeVariable_NotReplaced()
    {
        const string probe = """
                               from product in db.Products
                               where product.ListPrice > MinListPrice
                               orderby product.Name
                               select product
                               """;

        var stubbed = ProbeParameterStubber.Stub(
            probe,
            new ProbeStubContext(typeof(FakeAdventureWorksDbContext), "Product"));

        Assert.Contains("product.ListPrice", stubbed, StringComparison.Ordinal);
        Assert.Contains("product.Name", stubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("0.ListPrice", stubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("MinListPrice", stubbed, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(stubbed);
    }
}

public sealed class FakeGuidNoteDbContext : DbContext
{
    public FakeGuidNoteDbContext()
    {
    }

    public FakeGuidNoteDbContext(DbContextOptions<FakeGuidNoteDbContext> options)
        : base(options)
    {
    }

    public DbSet<FakeGuidUser> Users => Set<FakeGuidUser>();

    public DbSet<FakeGuidNote> Notes => Set<FakeGuidNote>();
}

public sealed class FakeGuidUser
{
    public Guid Id { get; set; }

    public string Username { get; set; } = string.Empty;
}

public sealed class FakeGuidNote
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
}