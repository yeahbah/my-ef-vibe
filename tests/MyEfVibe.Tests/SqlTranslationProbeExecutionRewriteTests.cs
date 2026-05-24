using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class SqlTranslationProbeExecutionRewriteTests
{
    [Fact]
    public void TryRewriteToEfStaticCalls_First_UsesReplQueryableRuntime()
    {
        const string snippet = "db.Users.First()";

        var rewritten = EfReplQueryableRewriter.TryRewriteToEfStaticCalls(snippet, typeof(FakeRewriterDbContext));

        Assert.Equal("global::MyEfVibe.ReplQueryableRuntime.First(db.Users)", rewritten);
    }

    [Fact]
    public void TryRewriteToEfStaticCalls_CursorPagination_AppDbContext()
    {
        var assemblyPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "CursorPaginationPg", "src", "Cursor.Pagination", "bin", "Debug", "net9.0", "CursorPagination.dll"));

        if (!File.Exists(assemblyPath))
            return;

        var contextType = Assembly.LoadFrom(assemblyPath).GetType("CursorPagination.Data.AppDbContext", throwOnError: true)!;

        var rewritten = EfReplQueryableRewriter.TryRewriteToEfStaticCalls("db.Users.First()", contextType);

        Assert.Equal("global::MyEfVibe.ReplQueryableRuntime.First(db.Users)", rewritten);
    }

    [Fact]
    public void TryCastDbSetRoots_Take_UsesReplQueryableRuntime()
    {
        var rewritten = EfReplQueryableRewriter.TryCastDbSetRoots(
            "db.Users.Take(10)",
            typeof(FakeRewriterDbContext));

        Assert.Equal("global::MyEfVibe.ReplQueryableRuntime.Take(db.Users, 10)", rewritten);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_BareWhere_UsesReplQueryableRuntime()
    {
        const string probe = "db.Users.Where(u => u.Id == key)";

        var normalized = SnippetNormalizer.ForEvaluation(probe, typeof(FakeRewriterDbContext));

        Assert.Contains("ReplQueryableRuntime.Where<", normalized, StringComparison.Ordinal);
        Assert.Contains("FakeRewriterUser>(db.Users, u => u.Id == key)", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreateProbeExpression_VarWhere_NormalizesToQueryableRuntime()
    {
        const string statement = "var entity = _context.Users.Where(x => x.Id == key);";

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(
            statement,
            representativeEntityTypeName: "FakeRewriterUser",
            dbContextType: typeof(FakeRewriterDbContext));

        Assert.NotNull(probe);

        var script = SnippetNormalizer.ForEvaluation(
            ProbeScriptFormatter.ToScriptExpression(probe),
            typeof(FakeRewriterDbContext));

        Assert.Contains("ReplQueryableRuntime.Where<", script, StringComparison.Ordinal);
        Assert.Contains(">(db.Users", script, StringComparison.Ordinal);
        Assert.Contains("Id == 0", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Enumerable", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_UsersProbe_AddsQueryablePipeline()
    {
        const string probe = "db.Users.Where(u => u.Id == Guid.Empty).Take(1)";

        var normalized = SnippetNormalizer.ForEvaluation(probe, typeof(FakeRewriterDbContext));

        Assert.StartsWith(
            "global::MyEfVibe.ReplQueryableRuntime.Take(global::MyEfVibe.ReplQueryableRuntime.Where<",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(">(db.Users", normalized, StringComparison.Ordinal);
        Assert.Contains(", 1)", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("IQueryable<", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("AsNoTracking", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_SelectUsernameProbe_RewritesQueryablePipeline()
    {
        const string probe =
            "db.Users.Where(u => u.Id == Guid.Empty).Select(u => u.Username).Take(1)";

        var normalized = SnippetNormalizer.ForEvaluation(probe, typeof(FakeGuidNoteDbContext));

        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.Take", normalized, StringComparison.Ordinal);
        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.Select", normalized, StringComparison.Ordinal);
        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.Where", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstOrDefaultAsync", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreateProbeExpression_UserNotesGetUser_ProducesTranslatableProbe()
    {
        const string statement = """
            await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == note.UserId)
            """;

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(
            statement,
            representativeEntityTypeName: "User",
            dbContextType: typeof(FakeGuidNoteDbContext),
            queryEntityTypeName: typeof(FakeGuidUser).FullName);

        Assert.NotNull(probe);
        Assert.Contains("Guid.Empty", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("note.UserId", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("AsNoTracking", probe, StringComparison.Ordinal);

        var script = SnippetNormalizer.ForEvaluation(
            ProbeScriptFormatter.ToScriptExpression(probe),
            typeof(FakeGuidNoteDbContext));

        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.Where", script, StringComparison.Ordinal);
        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.Take", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_RewritesTakeOnFinalLine()
    {
        var normalized = SnippetNormalizer.ForEvaluation("db.Users.Take(10);", typeof(FakeRewriterDbContext));

        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.Take(db.Users, 10)", normalized);
        Assert.DoesNotContain("IQueryable<", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_RewritesTakeThenToArray()
    {
        var normalized = SnippetNormalizer.ForEvaluation(
            "db.Users.Take(10).ToArray()",
            typeof(FakeRewriterDbContext));

        Assert.Equal(
            "global::MyEfVibe.ReplQueryableRuntime.ToArray(global::MyEfVibe.ReplQueryableRuntime.Take(db.Users, 10))",
            normalized);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_DoesNotLeaveToArrayOnObjectAfterTake()
    {
        var normalized = SnippetNormalizer.ForEvaluation(
            "db.Users.Take(10).ToArray()",
            typeof(FakeRewriterDbContext));

        Assert.DoesNotContain("Take(db.Cities, 10).ToArray()", normalized, StringComparison.Ordinal);
        Assert.Contains("ReplQueryableRuntime.ToArray", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_RewritesFirstOnFinalLine()
    {
        var normalized = SnippetNormalizer.ForEvaluation("db.Users.First();", typeof(FakeRewriterDbContext));

        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.First(db.Users)", normalized);
        Assert.DoesNotContain("Queryable.Take", normalized);
    }
}

public sealed class FakeRewriterDbContext : DbContext
{
    public FakeRewriterDbContext()
    {
    }

    public FakeRewriterDbContext(DbContextOptions<FakeRewriterDbContext> options)
        : base(options)
    {
    }

    public DbSet<FakeRewriterUser> Users => Set<FakeRewriterUser>();
}

public sealed class FakeRewriterUser
{
    public int Id { get; set; }
}
