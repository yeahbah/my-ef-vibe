namespace MyEfVibe.Tests;

public sealed class AppSettingsConnectionResolverTests
{
    [Fact]
    public void InferProvider_mysql_connection_string_without_dlls_is_mysql_not_sqlserver()
    {
        using var temp = new TempDirectory();

        var provider = AppSettingsConnectionResolver.InferProvider(
            temp.Path,
            "Server=localhost;Port=3306;Database=AdventureWorks2019;User=root;Password=secret;");

        Assert.Equal(MyEfVibeProvider.MySql, provider);
    }

    [Fact]
    public void InferProvider_mysql_deps_json_without_provider_dlls_is_mysql()
    {
        using var temp = new TempDirectory();

        File.WriteAllText(
            Path.Combine(temp.Path, "AdventureWorks.Infrastructure.Persistence.deps.json"),
            """
            {
              "targets": {
                ".NETCoreApp,Version=v10.0": {
                  "AdventureWorks.Infrastructure.Persistence/1.0.0": {
                    "dependencies": {
                      "MySql.EntityFrameworkCore": "10.0.7"
                    }
                  },
                  "MySql.EntityFrameworkCore/10.0.7": {}
                }
              }
            }
            """);

        var provider = AppSettingsConnectionResolver.InferProvider(
            temp.Path,
            "Server=localhost;Port=3306;Database=AdventureWorks2019;User=root;Password=secret;");

        Assert.Equal(MyEfVibeProvider.MySql, provider);
    }

    [Fact]
    public void InferProvider_sqlserver_connection_string_still_sqlserver()
    {
        using var temp = new TempDirectory();

        var provider = AppSettingsConnectionResolver.InferProvider(
            temp.Path,
            "Data Source=localhost,1533;Initial Catalog=WideWorldImporters;TrustServerCertificate=Yes");

        Assert.Equal(MyEfVibeProvider.SqlServer, provider);
    }

    [Fact]
    public void InferProvider_sqlserver_adventureworks_format_beats_mysql_deps_json()
    {
        using var temp = new TempDirectory();

        File.WriteAllText(
            Path.Combine(temp.Path, "AdventureWorks.Infrastructure.Persistence.deps.json"),
            """
            {
              "targets": {
                ".NETCoreApp,Version=v10.0": {
                  "AdventureWorks.Infrastructure.Persistence/1.0.0": {
                    "dependencies": {
                      "MySql.EntityFrameworkCore": "10.0.7"
                    }
                  },
                  "MySql.EntityFrameworkCore/10.0.7": {}
                }
              }
            }
            """);

        var provider = AppSettingsConnectionResolver.InferProvider(
            temp.Path,
            "Server=localhost,1433;Database=AdventureWorks;User Id=sa;Password=AdventureWorks_2022;Encrypt=false;TrustServerCertificate=true");

        Assert.Equal(MyEfVibeProvider.SqlServer, provider);
    }

    [Fact]
    public void TryResolve_adventureworks_mysql_appsettings()
    {
        using var temp = new TempDirectory();
        var startupProject = Path.Combine(temp.Path, "AdventureWorks.API.csproj");
        File.WriteAllText(startupProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        File.WriteAllText(
            Path.Combine(temp.Path, "appsettings.Development.json"),
            """
            {
              "ConnectionStrings": {
                "DefaultConnection": "Server=localhost;Port=3306;Database=AdventureWorks2019;User=root;Password=secret;"
              }
            }
            """);

        File.WriteAllText(
            Path.Combine(temp.Path, "AdventureWorks.Infrastructure.Persistence.deps.json"),
            """
            {
              "targets": {
                ".NETCoreApp,Version=v10.0": {
                  "AdventureWorks.Infrastructure.Persistence/1.0.0": {
                    "dependencies": {
                      "MySql.EntityFrameworkCore": "10.0.7"
                    }
                  },
                  "MySql.EntityFrameworkCore/10.0.7": {}
                }
              }
            }
            """);

        Assert.True(
            AppSettingsConnectionResolver.TryResolve(
                startupProject,
                Path.Combine(temp.Path),
                out var connectionString,
                out var provider));

        Assert.Contains("Port=3306", connectionString, StringComparison.Ordinal);
        Assert.Equal(MyEfVibeProvider.MySql, provider);
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
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }
}
