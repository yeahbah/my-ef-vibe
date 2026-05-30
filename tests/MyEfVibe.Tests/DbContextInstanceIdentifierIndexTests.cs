namespace MyEfVibe.Tests;

public sealed class DbContextInstanceIdentifierIndexTests
{
    [Fact]
    public void Build_discovers_custom_field_name_for_selected_context()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "OrderService.cs"),
            """
            using Microsoft.EntityFrameworkCore;

            public sealed class OrderService
            {
                private readonly NorthwindDbContext _store;
                public OrderService(NorthwindDbContext store) => _store = store;
            }
            """);

        var scope = new DbContextScanScope(
            "NorthwindDbContext",
            new HashSet<string>(["NorthwindDbContext"], StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));

        var index = DbContextInstanceIdentifierIndex.Build(
            File.ReadAllText(Path.Combine(temp.Path, "OrderService.cs")),
            scope);

        Assert.Contains("_store", index.SelectedContextIdentifiers);
        Assert.Contains("store", index.SelectedContextIdentifiers);
    }

    [Fact]
    public void StatementReferencesSelectedContextInstance_matches_custom_identifier()
    {
        using var temp = new TempDirectory();
        var source = """
                     using Microsoft.EntityFrameworkCore;
                     public sealed class OrderService
                     {
                         private readonly NorthwindDbContext _store;
                         public void Run() => _store.Products.Count();
                     }
                     """;

        File.WriteAllText(Path.Combine(temp.Path, "OrderService.cs"), source);

        var scope = new DbContextScanScope(
            "NorthwindDbContext",
            new HashSet<string>(["NorthwindDbContext"], StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));

        var index = DbContextInstanceIdentifierIndex.Build(source, scope);

        Assert.True(index.StatementReferencesSelectedContextInstance("_store.Products.Count()"));
    }

    private sealed class TempDirectory : IDisposable
    {
        internal TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "efvibe-tests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}