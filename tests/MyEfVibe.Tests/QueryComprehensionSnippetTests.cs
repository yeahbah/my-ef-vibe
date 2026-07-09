using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class QueryComprehensionSnippetTests
{
    [Fact]
    public void ForEvaluation_multiline_query_comprehension_does_not_use_repository_adapter()
    {
        const string snippet = """
                               from reviews in db.Products
                               select reviews
                               """;

        var normalized = SnippetNormalizer.ForEvaluation(snippet, typeof(FakeAdventureWorksDbContext));

        Assert.Contains("from reviews in db.Products", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("select reviews", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("global::MyEfVibe.ReplQueryableRuntime", normalized, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(normalized);
    }

    [Fact]
    public void ForEvaluation_single_line_query_comprehension_parses()
    {
        const string snippet = "from reviews in db.Products select reviews";

        var normalized = SnippetNormalizer.ForEvaluation(snippet, typeof(FakeAdventureWorksDbContext));

        Assert.Equal(snippet, normalized);
        ProbeTestHelper.AssertParsesAsScript(normalized);
    }

    [Fact]
    public void ForEvaluation_query_comprehension_with_external_parameter_stubs_parameter()
    {
        const string snippet = """
                               from product in db.Products
                               where product.ListPrice > MinListPrice
                               orderby product.Name
                               select product
                               """;

        var normalized = SnippetNormalizer.ForEvaluation(snippet, typeof(FakeAdventureWorksDbContext));

        Assert.Contains("product.ListPrice", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("MinListPrice", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("0.ListPrice", normalized, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(normalized);
    }

    [Fact]
    public void ForEvaluation_query_comprehension_with_where_parses()
    {
        const string snippet = """
                               from product in db.Products
                               where product.ListPrice > 0
                               select product
                               """;

        var normalized = SnippetNormalizer.ForEvaluation(snippet, typeof(FakeAdventureWorksDbContext));

        Assert.Contains("where product.ListPrice > 0", normalized, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(normalized);
    }

    [Fact]
    public void TryCreateProbeExpression_query_comprehension_returns_bare_queryable()
    {
        const string snippet = """
                               from reviews in db.Products
                               select reviews
                               """;

        var probe = SqlTranslationProbe.TryCreateProbeExpression(snippet);

        Assert.NotNull(probe);
        Assert.Contains("from reviews in db.Products", probe, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("select reviews", probe, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Query_comprehension_ToArray_translates_entity_columns_not_constant_zero()
    {
        var options = new DbContextOptionsBuilder<QueryCompProductDbContext>()
            .UseSqlite($"Data Source=query-comp-{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
            .Options;

        await using var context = new QueryCompProductDbContext(options);
        await context.Database.EnsureCreatedAsync();

        using var assemblyLoader = new InteractiveAssemblyLoader();
        assemblyLoader.RegisterDependency(typeof(QueryCompProductDbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(DbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(ReplQueryableRuntime).Assembly);

        var workspaceAssemblyPaths = new[]
            {
                typeof(QueryCompProductDbContext).Assembly.Location,
                typeof(DbContext).Assembly.Location,
                typeof(ReplQueryableRuntime).Assembly.Location,
            }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        var session = new ScriptSession(
            typeof(QueryCompProductDbContext),
            context,
            workspaceAssemblyPaths,
            assemblyLoader);

        const string snippet = """
                               (from reviews in db.Products
                               select reviews).ToArray()
                               """;

        var probe = SqlTranslationProbe.TryCreateProbeExpression(snippet);

        Assert.NotNull(probe);

        var sql = await session.EvaluateProbeAsync(ProbeScriptFormatter.ToQueryStringProbe(probe!));

        Assert.IsType<string>(sql);
        var sqlText = (string)sql!;

        Assert.DoesNotContain("SELECT 0", sqlText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Product", sqlText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForEvaluation_collapses_multiline_query_comprehension_to_single_line()
    {
        const string snippet = """
                               (from reviews in db.Products
                               select reviews).ToArray()
                               """;

        var normalized = SnippetNormalizer.ForEvaluation(snippet, typeof(FakeAdventureWorksDbContext));

        Assert.DoesNotContain('\n', normalized);
        Assert.Contains("from reviews in db.Products", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("select reviews", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".ToArray()", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Multiline_query_comprehension_ToArray_probe_translates_with_entity_projection_sql()
    {
        var options = new DbContextOptionsBuilder<QueryCompProductDbContext>()
            .UseSqlite($"Data Source=query-comp-{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
            .Options;

        await using var context = new QueryCompProductDbContext(options);
        await context.Database.EnsureCreatedAsync();

        using var assemblyLoader = new InteractiveAssemblyLoader();
        assemblyLoader.RegisterDependency(typeof(QueryCompProductDbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(DbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(ReplQueryableRuntime).Assembly);

        var workspaceAssemblyPaths = new[]
            {
                typeof(QueryCompProductDbContext).Assembly.Location,
                typeof(DbContext).Assembly.Location,
                typeof(ReplQueryableRuntime).Assembly.Location,
            }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        var session = new ScriptSession(
            typeof(QueryCompProductDbContext),
            context,
            workspaceAssemblyPaths,
            assemblyLoader);

        const string snippet = """
                               (from reviews in db.Products
                               select reviews).ToArray()
                               """;

        var normalized = SnippetNormalizer.ForEvaluation(snippet, typeof(QueryCompProductDbContext));
        var probe = SqlTranslationProbe.TryCreateProbeExpression(normalized);

        Assert.NotNull(probe);

        var sql = await session.EvaluateProbeAsync(ProbeScriptFormatter.ToQueryStringProbe(probe!));

        Assert.IsType<string>(sql);
        var sqlText = (string)sql!;

        Assert.DoesNotContain("SELECT 0", sqlText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Product", sqlText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToQueryStringProbe_wraps_bare_query_comprehension_before_member_access()
    {
        const string probe = """
                             from reviews in db.Products
                             select reviews
                             """;

        var expression = ProbeScriptFormatter.ToQueryStringProbe(probe);

        Assert.Equal(
            "(from reviews in db.Products select reviews).ToQueryString()",
            ProbeTestHelper.CollapseWhitespace(expression));
        ProbeTestHelper.AssertParsesAsScript(expression);
    }

    [Fact]
    public async Task Bare_multiline_query_comprehension_ToQueryString_evaluates_with_entity_projection_sql()
    {
        var options = new DbContextOptionsBuilder<QueryCompProductDbContext>()
            .UseSqlite($"Data Source=query-comp-{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
            .Options;

        await using var context = new QueryCompProductDbContext(options);
        await context.Database.EnsureCreatedAsync();

        using var assemblyLoader = new InteractiveAssemblyLoader();
        assemblyLoader.RegisterDependency(typeof(QueryCompProductDbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(DbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(ReplQueryableRuntime).Assembly);

        var workspaceAssemblyPaths = new[]
            {
                typeof(QueryCompProductDbContext).Assembly.Location,
                typeof(DbContext).Assembly.Location,
                typeof(ReplQueryableRuntime).Assembly.Location,
            }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        var session = new ScriptSession(
            typeof(QueryCompProductDbContext),
            context,
            workspaceAssemblyPaths,
            assemblyLoader);

        const string snippet = """
                               from reviews in db.Products
                               select reviews
                               """;

        var sql = await session.EvaluateAsync(ProbeScriptFormatter.ToQueryStringProbe(snippet));

        Assert.IsType<string>(sql);
        var sqlText = (string)sql!;

        Assert.DoesNotContain("SELECT 0", sqlText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Product", sqlText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Multiline_query_comprehension_ToQueryString_evaluates_with_entity_projection_sql()
    {
        var options = new DbContextOptionsBuilder<QueryCompProductDbContext>()
            .UseSqlite($"Data Source=query-comp-{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
            .Options;

        await using var context = new QueryCompProductDbContext(options);
        await context.Database.EnsureCreatedAsync();

        using var assemblyLoader = new InteractiveAssemblyLoader();
        assemblyLoader.RegisterDependency(typeof(QueryCompProductDbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(DbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(ReplQueryableRuntime).Assembly);

        var workspaceAssemblyPaths = new[]
            {
                typeof(QueryCompProductDbContext).Assembly.Location,
                typeof(DbContext).Assembly.Location,
                typeof(ReplQueryableRuntime).Assembly.Location,
            }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        var session = new ScriptSession(
            typeof(QueryCompProductDbContext),
            context,
            workspaceAssemblyPaths,
            assemblyLoader);

        const string snippet = """
                               (from reviews in db.Products
                               select reviews).ToQueryString()
                               """;

        var sql = await session.EvaluateAsync(snippet);

        Assert.IsType<string>(sql);
        var sqlText = (string)sql!;

        Assert.DoesNotContain("SELECT 0", sqlText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Product", sqlText, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class QueryCompProduct
{
    public int ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal ListPrice { get; set; }
}

public sealed class QueryCompProductDbContext(DbContextOptions<QueryCompProductDbContext> options)
    : DbContext(options)
{
    public DbSet<QueryCompProduct> Products => Set<QueryCompProduct>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<QueryCompProduct>(entity =>
        {
            entity.HasKey(product => product.ProductId);
            entity.ToTable("Product", "Production");
            entity.Property(product => product.ProductId).HasColumnName("ProductID");
        });
    }
}
