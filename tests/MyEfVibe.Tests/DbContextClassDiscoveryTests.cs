namespace MyEfVibe.Tests;

public sealed class DbContextClassDiscoveryTests
{
    [Fact]
    public void DiscoverDbContextTypeAliases_includes_selected_context_interface()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "NorthwindDbContext.cs"),
            """
            using Microsoft.EntityFrameworkCore;

            public sealed class NorthwindDbContext : DbContext, INorthwindDbContext
            {
            }

            public interface INorthwindDbContext
            {
            }
            """);

        var aliasesByType = DbContextClassDiscovery.DiscoverDbContextTypeAliases([temp.Path]);

        Assert.True(aliasesByType.TryGetValue("NorthwindDbContext", out var aliases));
        Assert.Contains("NorthwindDbContext", aliases);
        Assert.Contains("INorthwindDbContext", aliases);
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