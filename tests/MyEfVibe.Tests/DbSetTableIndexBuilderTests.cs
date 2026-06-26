using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class DbSetTableIndexBuilderTests
{
    private sealed class Product
    {
        public int ProductId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class AdventureContext : DbContext
    {
        public AdventureContext(DbContextOptions<AdventureContext> options)
            : base(options)
        {
        }

        public DbSet<Product> Products => Set<Product>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>(entity =>
            {
                entity.HasKey(product => product.ProductId);
                entity.ToTable("Product", "Production");
            });
        }
    }

    [Fact]
    public void Build_registers_dbset_entity_and_relational_aliases()
    {
        var options = new DbContextOptionsBuilder<AdventureContext>()
            .UseSqlite($"Data Source=dbset-index-{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
            .Options;

        using var dbContext = new AdventureContext(options);

        var index = DbSetTableIndexBuilder.Build(dbContext);

        Assert.True(index.ContainsKey("Products"));
        Assert.True(index.ContainsKey("Product"));
    }

    [Fact]
    public void Build_registers_live_sqlite_table_names()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"dbset-index-live-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AdventureContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        try
        {
            using (var dbContext = new AdventureContext(options))
            {
                dbContext.Database.EnsureCreated();
            }

            using (var dbContext = new AdventureContext(options))
            {
                var index = DbSetTableIndexBuilder.Build(dbContext);

                Assert.True(index.ContainsKey("Product"));
                Assert.True(index.ContainsKey("Products"));
            }
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}
