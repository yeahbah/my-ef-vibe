using Microsoft.EntityFrameworkCore;
using MyEfVibe.Reporters;

namespace MyEfVibe.Tests;

public sealed class ErDiagramMermaidBuilderTests
{
    [Fact]
    public void Build_includes_entities_columns_and_relationships()
    {
        var options = new DbContextOptionsBuilder<DiagramTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new DiagramTestDbContext(options);

        var childModelEntity = dbContext.Model.FindEntityType(typeof(DiagramChild));
        Assert.NotNull(childModelEntity);
        Assert.NotEmpty(childModelEntity!.GetForeignKeys());

        var mermaid = ErDiagramMermaidBuilder.Build(dbContext);

        Assert.Contains("erDiagram", mermaid, StringComparison.Ordinal);
        Assert.Contains("DiagramParent", mermaid, StringComparison.Ordinal);
        Assert.Contains("DiagramChild", mermaid, StringComparison.Ordinal);
        Assert.Contains("ParentId", mermaid, StringComparison.Ordinal);
        Assert.Contains("||--", mermaid, StringComparison.Ordinal);
        Assert.Contains("Parent", mermaid, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_filtered_to_entity_excludes_unrelated_tables()
    {
        var options = new DbContextOptionsBuilder<DiagramTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new DiagramTestDbContext(options);

        var full = ErDiagramMermaidBuilder.Build(dbContext);
        var filtered = ErDiagramMermaidBuilder.Build(dbContext, "Children");

        Assert.Contains("DiagramUnrelated", full, StringComparison.Ordinal);
        Assert.Contains("DiagramChild", filtered, StringComparison.Ordinal);
        Assert.Contains("DiagramParent", filtered, StringComparison.Ordinal);
        Assert.DoesNotContain("DiagramUnrelated", filtered, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagramJsonReporter_Build_returns_mermaid_payload()
    {
        var options = new DbContextOptionsBuilder<DiagramTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new DiagramTestDbContext(options);

        var payload = DiagramJsonReporter.Build(dbContext);

        Assert.True(payload.Success);
        Assert.Equal("mermaid", payload.Format);
        Assert.Equal("DiagramTestDbContext", payload.DbContext);
        Assert.Contains("erDiagram", payload.Content, StringComparison.Ordinal);
    }
}

public sealed class DiagramTestDbContext : DbContext
{
    public DiagramTestDbContext(DbContextOptions<DiagramTestDbContext> options)
        : base(options)
    {
    }

    public DbSet<DiagramParent> Parents => Set<DiagramParent>();

    public DbSet<DiagramChild> Children => Set<DiagramChild>();

    public DbSet<DiagramUnrelated> Unrelated => Set<DiagramUnrelated>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DiagramChild>()
            .HasOne(child => child.Parent)
            .WithMany(parent => parent.Children)
            .HasForeignKey(child => child.ParentId);
    }
}

public sealed class DiagramParent
{
    public int Id { get; set; }

    public ICollection<DiagramChild> Children { get; set; } = [];
}

public sealed class DiagramChild
{
    public int Id { get; set; }

    public int ParentId { get; set; }

    public DiagramParent Parent { get; set; } = null!;
}

public sealed class DiagramUnrelated
{
    public int Id { get; set; }
}
