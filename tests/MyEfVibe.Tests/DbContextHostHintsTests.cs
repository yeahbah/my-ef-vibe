namespace MyEfVibe.Tests;

public sealed class DbContextHostHintsTests
{
    [Fact]
    public void TryReadDatabaseProviderName_reads_postgresql_from_appsettings()
    {
        using var temp = new TempDirectory();
        var startupProject = Path.Combine(temp.Path, "AdventureWorks.API.csproj");
        File.WriteAllText(startupProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        File.WriteAllText(
            Path.Combine(temp.Path, "appsettings.Development.json"),
            """
            {
              "EntityFrameworkCoreSettings": {
                "DatabaseProvider": "PostgreSQL"
              }
            }
            """);

        var provider = AppSettingsConnectionResolver.TryReadDatabaseProviderName(startupProject);

        Assert.Equal("PostgreSQL", provider);
    }

    [Fact]
    public void MapDatabaseProviderName_postgresql_is_npgsql()
    {
        using var temp = new TempDirectory();
        var startupProject = Path.Combine(temp.Path, "App.csproj");
        File.WriteAllText(startupProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(
            Path.Combine(temp.Path, "appsettings.json"),
            """
            {
              "EntityFrameworkCoreSettings": { "DatabaseProvider": "PostgreSQL" },
              "ConnectionStrings": { "DefaultConnection": "Host=localhost;Database=aw;" }
            }
            """);

        Assert.True(
            AppSettingsConnectionResolver.TryResolve(
                startupProject,
                temp.Path,
                out _,
                out var provider));

        Assert.Equal(MyEfVibeProvider.Npgsql, provider);
    }

    [Fact]
    public void TryApplyPostgreSqlNamingHint_sets_database_provider_field()
    {
        var context = new AdventureWorksStyleDbContext();

        DbContextHostHints.TryApplyPostgreSqlNamingHint(
            context,
            string.Empty,
            MyEfVibeProvider.Npgsql);

        Assert.Equal("PostgreSQL", context.GetDatabaseProvider());
    }

    private sealed class AdventureWorksStyleDbContext
    {
        private readonly string _databaseProvider = "SqlServer";

        internal string GetDatabaseProvider()
        {
            return _databaseProvider;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        internal TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "myefvibe-tests",
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
            catch
            {
                // best effort
            }
        }
    }
}