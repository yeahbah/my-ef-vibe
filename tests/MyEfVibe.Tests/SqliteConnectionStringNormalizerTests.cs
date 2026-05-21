namespace MyEfVibe.Tests;

public sealed class SqliteConnectionStringNormalizerTests
{
    [Fact]
    public void Normalize_resolves_relative_data_source_from_repo_root()
    {
        using var temp = new TempDirectory();
        var databasePath = Path.Combine(temp.Path, "Source", "AdventureWorksLT.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        File.WriteAllText(databasePath, string.Empty);

        var startupProject = Path.Combine(temp.Path, "apps", "api", "AdventureWorks.API.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(startupProject)!);
        File.WriteAllText(
            startupProject,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var normalized = SqliteConnectionStringNormalizer.Normalize(
            "Data Source=Source/AdventureWorksLT.db",
            startupProject);

        Assert.Equal($"Data Source={databasePath}", normalized);
    }

    [Fact]
    public void Normalize_preserves_absolute_path()
    {
        using var temp = new TempDirectory();
        var databasePath = Path.Combine(temp.Path, "data.db");
        File.WriteAllText(databasePath, string.Empty);

        var startupProject = Path.Combine(temp.Path, "App.csproj");
        File.WriteAllText(
            startupProject,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var normalized = SqliteConnectionStringNormalizer.Normalize(
            $"Data Source={databasePath}",
            startupProject);

        Assert.Equal($"Data Source={databasePath}", normalized);
    }

    [Fact]
    public void AppSettings_resolver_layers_development_over_base()
    {
        using var temp = new TempDirectory();
        var startupProject = Path.Combine(temp.Path, "AdventureWorks.API.csproj");
        File.WriteAllText(
            startupProject,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(
            Path.Combine(temp.Path, "appsettings.json"),
            """
            {
              "ConnectionStrings": {
                "DefaultConnection": "Data Source=Source/missing.db"
              }
            }
            """);

        var developmentDatabase = Path.Combine(temp.Path, "dev.db");
        File.WriteAllText(developmentDatabase, string.Empty);

        File.WriteAllText(
            Path.Combine(temp.Path, "appsettings.Development.json"),
            $$"""
            {
              "ConnectionStrings": {
                "DefaultConnection": "Data Source={{developmentDatabase}}"
              }
            }
            """);

        Assert.True(
            AppSettingsConnectionResolver.TryResolve(
                startupProject,
                efOutputDirectory: temp.Path,
                out var connectionString,
                out var provider));

        Assert.Equal($"Data Source={developmentDatabase}", connectionString);
        Assert.Equal(MyEfVibeProvider.Sqlite, provider);
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
                Directory.Delete(Path, recursive: true);
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
