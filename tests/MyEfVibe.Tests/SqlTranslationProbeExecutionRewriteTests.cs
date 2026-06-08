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
        {
            return;
        }

        var contextType = Assembly.LoadFrom(assemblyPath).GetType("CursorPagination.Data.AppDbContext", true)!;

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
            "FakeRewriterUser",
            typeof(FakeRewriterDbContext));

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
            "User",
            typeof(FakeGuidNoteDbContext),
            typeof(FakeGuidUser).FullName);

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
    public void SnippetNormalizer_ForEvaluation_SelectOrderByToList_UsesExpressionOrderBy()
    {
        const string probe = "db.Users.Select(x => x.Name).OrderBy(x => x).ToList()";

        var normalized = SnippetNormalizer.ForEvaluation(probe, typeof(FakeRewriterDbContext));

        Assert.StartsWith("global::MyEfVibe.ReplQueryableRuntime.ToList(", normalized, StringComparison.Ordinal);
        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.OrderBy<global::System.String, global::System.String>(",
            normalized, StringComparison.Ordinal);
        Assert.Contains("x => x)", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_TakeSelectOrderByToList_DoesNotCallSelectOnObject()
    {
        const string probe =
            "db.Users.Take(10).Select(x => new { x.Id, x.Name }).OrderBy(x => x.Name).ToList()";

        var normalized = SnippetNormalizer.ForEvaluation(probe, typeof(FakeRewriterDbContext));

        Assert.StartsWith("global::MyEfVibe.ReplQueryableRuntime.ToList(", normalized, StringComparison.Ordinal);
        Assert.Contains(
            "((global::System.Linq.IQueryable<global::MyEfVibe.Tests.FakeRewriterUser>)(global::MyEfVibe.ReplQueryableRuntime.Take(db.Users, 10))).Select(x => new { x.Id, x.Name })",
            normalized, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(x => x.Name)", normalized, StringComparison.Ordinal);
        Assert.Contains("x => new { x.Id, x.Name }", normalized, StringComparison.Ordinal);
        Assert.Contains("x => x.Name", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("Take(db.Users, 10).Select(", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_TakeSelectAnonymousToList_UsesQueryableCastForAnonymousProjection()
    {
        const string probe = "db.Users.Take(10).Select(x => new { x.Id, x.Name }).ToList()";

        var normalized = SnippetNormalizer.ForEvaluation(probe, typeof(FakeRewriterDbContext));

        Assert.StartsWith("global::MyEfVibe.ReplQueryableRuntime.ToList(", normalized, StringComparison.Ordinal);
        Assert.Contains(
            "((global::System.Linq.IQueryable<global::MyEfVibe.Tests.FakeRewriterUser>)(global::MyEfVibe.ReplQueryableRuntime.Take(db.Users, 10))).Select(x => new { x.Id, x.Name })",
            normalized, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ReplQueryableRuntime.Select(global::MyEfVibe.ReplQueryableRuntime.Take",
            normalized,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_AsNoTrackingTakeOrderBy_UsesQueryablePipeline()
    {
        const string probe =
            "db.Users.AsNoTracking().Take(10).OrderBy(x => x.Name)";

        var normalized = SnippetNormalizer.ForEvaluation(probe, typeof(FakeRewriterDbContext));

        Assert.Contains("ReplQueryableRuntime.OrderBy<", normalized, StringComparison.Ordinal);
        Assert.Contains("FakeRewriterUser, global::System.String>", normalized, StringComparison.Ordinal);
        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.Take(db.Users, 10)", normalized,
            StringComparison.Ordinal);
        Assert.Contains("x => x.Name", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("AsNoTracking", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("Enumerable", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_OrderByAfterToList_DoesNotUseQueryableRuntime()
    {
        var normalized = SnippetNormalizer.ForEvaluation(
            "db.Users.Take(10).ToList().OrderBy(x => x.Name)",
            typeof(FakeRewriterDbContext));

        Assert.DoesNotContain("ReplQueryableRuntime.OrderBy", normalized, StringComparison.Ordinal);
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

    [Fact]
    public void SnippetNormalizer_ForEvaluation_RewritesCountPredicateToQueryableRuntime()
    {
        var normalized = SnippetNormalizer.ForEvaluation(
            "db.Users.Count(u => u.Id == 1)",
            typeof(FakeRewriterDbContext));

        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.Count<", normalized, StringComparison.Ordinal);
        Assert.Contains("FakeRewriterUser>", normalized, StringComparison.Ordinal);
        Assert.Contains("db.Users, u => u.Id == 1", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("Enumerable", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_RewritesWhereToListToQueryableRuntime()
    {
        var normalized = SnippetNormalizer.ForEvaluation(
            "db.Users.Where(u => u.Id == 1).ToList()",
            typeof(FakeRewriterDbContext));

        Assert.StartsWith("global::MyEfVibe.ReplQueryableRuntime.ToList(", normalized, StringComparison.Ordinal);
        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.Where<", normalized, StringComparison.Ordinal);
        Assert.Contains("FakeRewriterUser>(db.Users, u => u.Id == 1)", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("Enumerable", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_RewritesWhereToArrayToQueryableRuntime()
    {
        var normalized = SnippetNormalizer.ForEvaluation(
            "db.Users.Where(u => u.Id == 1).ToArray()",
            typeof(FakeRewriterDbContext));

        Assert.StartsWith("global::MyEfVibe.ReplQueryableRuntime.ToArray(", normalized, StringComparison.Ordinal);
        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.Where<", normalized, StringComparison.Ordinal);
        Assert.Contains("FakeRewriterUser>(db.Users, u => u.Id == 1)", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_RewritesWhereCountToQueryableRuntime()
    {
        var normalized = SnippetNormalizer.ForEvaluation(
            "db.Users.Where(u => u.Id == 1).Count()",
            typeof(FakeRewriterDbContext));

        Assert.StartsWith("global::MyEfVibe.ReplQueryableRuntime.Count(", normalized, StringComparison.Ordinal);
        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.Where<", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("Enumerable", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_RewritesProjectionBeforeWhereWithoutCallingSelectOnObject()
    {
        var normalized = SnippetNormalizer.ForEvaluation(
            "db.Users.Select(x => new { x.Id, x.Name }).Where(x => x.Name.Contains(\"Crankarm\")).Take(10).ToList();",
            typeof(FakeRewriterDbContext));

        Assert.StartsWith("global::MyEfVibe.ReplQueryableRuntime.ToList(", normalized, StringComparison.Ordinal);
        Assert.Contains("global::MyEfVibe.ReplQueryableRuntime.Take(", normalized, StringComparison.Ordinal);
        Assert.Contains("db.Users", normalized, StringComparison.Ordinal);
        Assert.Contains(".Select(x => new { x.Id, x.Name })", normalized, StringComparison.Ordinal);
        Assert.Contains(".Where(x => x.Name.Contains(\"Crankarm\"))", normalized, StringComparison.Ordinal);
        Assert.Contains(", 10)", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("ReplQueryableRuntime.Where<", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain(".Select(", normalized[..normalized.IndexOf("db.Users", StringComparison.Ordinal)]);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_DoesNotTypeProjectionTerminalPredicateAsEntity()
    {
        var normalized = SnippetNormalizer.ForEvaluation(
            "db.Users.Select(x => new { x.Id, x.Name }).Take(10).FirstOrDefault(x => x.Name.Contains(\"Crankarm\"));",
            typeof(FakeRewriterDbContext));

        Assert.Equal(
            "db.Users.Select(x => new { x.Id, x.Name }).Take(10).FirstOrDefault(x => x.Name.Contains(\"Crankarm\"))",
            normalized);
        Assert.DoesNotContain("ReplQueryableRuntime.FirstOrDefault<", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("FakeRewriterUser", normalized, StringComparison.Ordinal);
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

    public string Name { get; set; } = string.Empty;
}