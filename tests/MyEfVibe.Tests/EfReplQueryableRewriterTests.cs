namespace MyEfVibe.Tests;

public sealed class EfReplQueryableRewriterTests
{
    [Fact]
    public void TryRewriteBareWhere_on_DbSet_uses_typed_runtime_call()
    {
        const string probe = "db.Users.Where(u => u.Id == 0)";

        var rewritten = EfReplQueryableRewriter.TryRewriteBareWhere(probe, typeof(FakeRewriterDbContext));

        Assert.Contains("ReplQueryableRuntime.Where<", rewritten, StringComparison.Ordinal);
        Assert.Contains("FakeRewriterUser>(db.Users, u => u.Id == 0)", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRewriteBareWhere_on_include_chain_uses_typed_runtime_call()
    {
        const string probe =
            "db.Employees.Include(e => e.PersonBusinessEntity).Where(e => e.BusinessEntityId == 0)";

        var rewritten = EfReplQueryableRewriter.TryRewriteBareWhere(probe, typeof(FakeAdventureWorksDbContext));

        Assert.NotNull(rewritten);
        Assert.Contains("ReplQueryableRuntime.Where<", rewritten, StringComparison.Ordinal);
        Assert.Contains("FakeEmployee>", rewritten, StringComparison.Ordinal);
        Assert.Contains(".Include(e => e.PersonBusinessEntity)", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRewriteToEfStaticCalls_include_chain_with_terminal_uses_typed_first_or_default()
    {
        const string snippet =
            "db.Employees.Include(e => e.PersonBusinessEntity).Where(e => e.BusinessEntityId == 0).FirstOrDefault()";

        var rewritten = EfReplQueryableRewriter.TryRewriteToEfStaticCalls(snippet, typeof(FakeAdventureWorksDbContext));

        Assert.NotNull(rewritten);
        Assert.Contains("ReplQueryableRuntime.FirstOrDefault(", rewritten, StringComparison.Ordinal);
        Assert.Contains("db.Employees.Include(e => e.PersonBusinessEntity)", rewritten, StringComparison.Ordinal);
        Assert.Contains("BusinessEntityId == 0", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRewriteToEfStaticCalls_terminal_with_lambda_predicate_uses_typed_call()
    {
        const string snippet =
            "db.Employees.FirstOrDefault(e => e.BusinessEntityId == 0, cancellationToken)";

        var rewritten = EfReplQueryableRewriter.TryRewriteToEfStaticCalls(snippet, typeof(FakeAdventureWorksDbContext));

        Assert.NotNull(rewritten);
        Assert.Contains("FirstOrDefault<", rewritten, StringComparison.Ordinal);
        Assert.Contains("FakeEmployee>", rewritten, StringComparison.Ordinal);
        Assert.Contains("BusinessEntityId == 0", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRewriteWhereTakePipeline_with_select_uses_typed_select_and_where()
    {
        const string probe = "db.Users.Where(u => u.Id == 0).Select(u => u.Username).Take(1)";

        var rewritten = EfReplQueryableRewriter.TryRewriteWhereTakePipeline(probe, typeof(FakeGuidNoteDbContext));

        Assert.NotNull(rewritten);
        Assert.Contains("ReplQueryableRuntime.Take(", rewritten, StringComparison.Ordinal);
        Assert.Contains("ReplQueryableRuntime.Select<", rewritten, StringComparison.Ordinal);
        Assert.Contains("ReplQueryableRuntime.Where<", rewritten, StringComparison.Ordinal);
        Assert.Contains("FakeGuidUser>", rewritten, StringComparison.Ordinal);
        Assert.Contains("System.String>", rewritten, StringComparison.Ordinal);
    }
}
