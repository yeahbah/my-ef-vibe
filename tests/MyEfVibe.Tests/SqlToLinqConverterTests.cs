namespace MyEfVibe.Tests;

using Microsoft.EntityFrameworkCore;

public sealed class SqlToLinqConverterTests
{
    private sealed class FakeProduct
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class FakeDbContext
    {
        public IQueryable<FakeProduct> Products => Array.Empty<FakeProduct>().AsQueryable();
    }

    private sealed class SchemaProduct
    {
        public int ProductId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class SchemaDbContext : DbContext
    {
        public SchemaDbContext(DbContextOptions<SchemaDbContext> options)
            : base(options)
        {
        }

        public DbSet<SchemaProduct> Products => Set<SchemaProduct>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SchemaProduct>(entity =>
            {
                entity.HasKey(product => product.ProductId);
                entity.ToTable("Product", "Production");
                entity.Property(product => product.ProductId).HasColumnName("ProductID");
            });
        }
    }

    [Fact]
    public void Convert_maps_simple_select_with_where_and_top()
    {
        var draft = SqlToLinqConverter.Convert(
            new FakeDbContext(),
            "SELECT TOP 10 * FROM Products WHERE Name = 'Helmet'");

        Assert.Contains("db.Products", draft.Linq, StringComparison.Ordinal);
        Assert.Contains(".Where(x => x.Name == \"Helmet\")", draft.Linq, StringComparison.Ordinal);
        Assert.Contains(".Take(10)", draft.Linq, StringComparison.Ordinal);
        Assert.Equal("high", draft.Confidence);
    }

    [Fact]
    public void Convert_maps_postgres_quoted_schema_table_with_limit_and_projection()
    {
        var options = new DbContextOptionsBuilder<SchemaDbContext>()
            .UseSqlite($"Data Source=sqltolinq-{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
            .Options;

        using var dbContext = new SchemaDbContext(options);

        var draft = SqlToLinqConverter.Convert(
            dbContext,
            """
            select "ProductID", "Name" FROM "Production"."Product"
            limit 10
            """);

        Assert.Contains("db.Products", draft.Linq, StringComparison.Ordinal);
        Assert.Contains(".Take(10)", draft.Linq, StringComparison.Ordinal);
        Assert.Contains("ProductId", draft.Linq, StringComparison.Ordinal);
        Assert.Contains("Name", draft.Linq, StringComparison.Ordinal);
        Assert.Equal("high", draft.Confidence);
        Assert.Contains(draft.Mappings, mapping => mapping.Table == "Production.Product");
    }

    [Fact]
    public void Convert_maps_sqlite_quoted_dotted_table_name()
    {
        var options = new DbContextOptionsBuilder<SchemaDbContext>()
            .UseSqlite($"Data Source=sqltolinq-quoted-{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
            .Options;

        using var dbContext = new SchemaDbContext(options);

        var draft = SqlToLinqConverter.Convert(
            dbContext,
            """
            SELECT "ProductID", "Name"
            FROM "Production.Product"
            LIMIT 10
            """);

        Assert.Contains("db.Products", draft.Linq, StringComparison.Ordinal);
        Assert.Contains(".Take(10)", draft.Linq, StringComparison.Ordinal);
        Assert.Equal("high", draft.Confidence);
        Assert.Contains(draft.Mappings, mapping => mapping.Table == "Production.Product");
    }

    [Fact]
    public void Convert_reports_missing_from_clause()
    {
        var draft = SqlToLinqConverter.Convert(new FakeDbContext(), "SELECT 1");

        Assert.Contains("FROM clause", draft.Unsupported[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("low", draft.Confidence);
    }
}

public sealed class SqlSimilarityTests
{
    [Fact]
    public void Compare_returns_higher_score_for_similar_sql()
    {
        var score = SqlSimilarity.Compare(
            "SELECT Id, Name FROM Products WHERE Name = 'x'",
            "SELECT [p].[Id], [p].[Name] FROM [Products] AS [p] WHERE [p].[Name] = @p0");

        Assert.True(score > 0.2);
    }
}

public sealed class ServeProtocolTests_SqlToLinq
{
    [Fact]
    public void TryParseRequest_parses_sqlToLinq()
    {
        var request = ServeProtocol.TryParseRequest("""{"type":"sqlToLinq","sql":"SELECT * FROM Products"}""");

        Assert.NotNull(request);
        Assert.Equal("sqlToLinq", request!.Type);
        Assert.Equal("SELECT * FROM Products", request.Sql);
    }
}
