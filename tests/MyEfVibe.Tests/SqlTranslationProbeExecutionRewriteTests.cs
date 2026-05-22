using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class SqlTranslationProbeExecutionRewriteTests
{
    [Fact]
    public void TryRewriteToEfStaticCalls_First_UsesEntityFrameworkQueryableExtensions()
    {
        const string snippet = "db.Users.First()";

        var rewritten = EfReplQueryableRewriter.TryRewriteToEfStaticCalls(snippet, typeof(FakeRewriterDbContext));

        Assert.Equal(
            "System.Linq.Queryable.FirstOrDefault("
            + "((System.Linq.IQueryable<MyEfVibe.Tests.FakeRewriterUser>)db.Users))",
            rewritten);
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

        Assert.Equal(
            "System.Linq.Queryable.FirstOrDefault("
            + "((System.Linq.IQueryable<CursorPagination.Models.User>)db.Users))",
            rewritten);
    }

    [Fact]
    public void TryCastDbSetRoots_Take_AddsQueryableCast()
    {
        var rewritten = EfReplQueryableRewriter.TryCastDbSetRoots(
            "db.Users.Take(10)",
            typeof(FakeRewriterDbContext));

        Assert.Equal(
            "((System.Linq.IQueryable<MyEfVibe.Tests.FakeRewriterUser>)db.Users).Take(10)",
            rewritten);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_UsersProbe_AddsQueryableCast()
    {
        const string probe = "db.Users.Where(u => u.Id == Guid.Empty).Take(1)";

        var normalized = SnippetNormalizer.ForEvaluation(probe, typeof(FakeRewriterDbContext));

        Assert.StartsWith(
            "System.Linq.Queryable.Take(System.Linq.Queryable.Where("
            + "((System.Linq.IQueryable<MyEfVibe.Tests.FakeRewriterUser>)db.Users)",
            normalized,
            StringComparison.Ordinal);
        Assert.Contains(", 1)", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("AsNoTracking", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_SelectUsernameProbe_RewritesQueryablePipeline()
    {
        const string probe =
            "db.Users.Where(u => u.Id == Guid.Empty).Select(u => u.Username).Take(1)";

        var normalized = SnippetNormalizer.ForEvaluation(probe, typeof(FakeGuidNoteDbContext));

        Assert.Contains("System.Linq.Queryable.Take", normalized, StringComparison.Ordinal);
        Assert.Contains("System.Linq.Queryable.Select", normalized, StringComparison.Ordinal);
        Assert.Contains("System.Linq.Queryable.Where", normalized, StringComparison.Ordinal);
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

        Assert.Contains("System.Linq.Queryable.Where", script, StringComparison.Ordinal);
        Assert.Contains("System.Linq.Queryable.Take", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_RewritesTakeOnFinalLine()
    {
        var normalized = SnippetNormalizer.ForEvaluation("db.Users.Take(10);", typeof(FakeRewriterDbContext));

        Assert.Contains("IQueryable<MyEfVibe.Tests.FakeRewriterUser>", normalized);
        Assert.Contains(".Take(10)", normalized);
    }

    [Fact]
    public void SnippetNormalizer_ForEvaluation_RewritesFirstOnFinalLine()
    {
        var normalized = SnippetNormalizer.ForEvaluation("db.Users.First();", typeof(FakeRewriterDbContext));

        Assert.Contains("System.Linq.Queryable.FirstOrDefault", normalized);
        Assert.DoesNotContain("Queryable.Take", normalized);
    }
}

public sealed class FakeRewriterDbContext : DbContext
{
    public DbSet<FakeRewriterUser> Users => Set<FakeRewriterUser>();
}

public sealed class FakeRewriterUser
{
    public int Id { get; set; }
}
