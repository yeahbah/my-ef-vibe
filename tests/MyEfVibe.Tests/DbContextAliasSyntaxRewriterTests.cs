namespace MyEfVibe.Tests;

public sealed class DbContextAliasSyntaxRewriterTests
{
    [Fact]
    public void Rewrite_DbContextMemberAccess_BecomesDb()
    {
        const string code = "DbContext.Employees.Include(e => e.Department)";

        var rewritten = DbContextAliasSyntaxRewriter.Rewrite(code);

        Assert.StartsWith("db.Employees", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void Rewrite_LambdaParameterE_NotRewritten()
    {
        const string code = "db.Employees.Where(e => e.DepartmentId == departmentId)";

        var rewritten = DbContextAliasSyntaxRewriter.Rewrite(code);

        Assert.Contains("e => e.DepartmentId", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void Rewrite_ThisDbContext_BecomesDb()
    {
        const string code = "this._dbContext.Products.Take(10)";

        var rewritten = DbContextAliasSyntaxRewriter.Rewrite(code);

        Assert.StartsWith("db.Products", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void Rewrite_custom_discovered_identifier_becomes_db()
    {
        const string code = "_store.Products.Take(10)";

        var rewritten = DbContextAliasSyntaxRewriter.Rewrite(code, ["_store"]);

        Assert.StartsWith("db.Products", rewritten, StringComparison.Ordinal);
    }
}
