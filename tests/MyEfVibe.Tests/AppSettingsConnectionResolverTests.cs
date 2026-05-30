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
    public void InferProvider_oracle_connection_string_is_oracle_not_sqlserver()
    {
        using var temp = new TempDirectory();

        var provider = AppSettingsConnectionResolver.InferProvider(
            temp.Path,
            "User Id=adventureworks;Password=secret;Data Source=localhost:1521/FREEPDB1;");

        Assert.Equal(MyEfVibeProvider.Oracle, provider);
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

    [Fact]
    public void TryResolve_adventureworks_oracle_appsettings()
    {
        using var temp = new TempDirectory();
        var startupProject = Path.Combine(temp.Path, "AdventureWorks.API.csproj");
        File.WriteAllText(startupProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        File.WriteAllText(
            Path.Combine(temp.Path, "appsettings.Development.json"),
            """
            {
              "ConnectionStrings": {
                "DefaultConnection": "User Id=adventureworks;Password=secret;Data Source=localhost:1521/FREEPDB1;"
              }
            }
            """);

        Assert.True(
            AppSettingsConnectionResolver.TryResolve(
                startupProject,
                Path.Combine(temp.Path),
                out var connectionString,
                out var provider));

        Assert.Contains("FREEPDB1", connectionString, StringComparison.Ordinal);
        Assert.Equal(MyEfVibeProvider.Oracle, provider);
    }

    [Fact]
    public void TryResolve_environment_specific_oracle_appsettings_overrides_development_sqlserver()
    {
        using var environment = new EnvironmentVariableScope("ASPNETCORE_ENVIRONMENT", "Oracle");
        using var temp = new TempDirectory();
        var startupProject = Path.Combine(temp.Path, "AdventureWorks.API.csproj");
        File.WriteAllText(startupProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        File.WriteAllText(
            Path.Combine(temp.Path, "appsettings.Development.json"),
            """
            {
              "ConnectionStrings": {
                "DefaultConnection": "Server=localhost,1433;Database=AdventureWorks;User Id=sa;Password=AdventureWorks_2022;Encrypt=false;TrustServerCertificate=true"
              }
            }
            """);

        File.WriteAllText(
            Path.Combine(temp.Path, "appsettings.Oracle.json"),
            """
            {
              "Database": {
                "Provider": "Oracle"
              },
              "ConnectionStrings": {
                "DefaultConnection": "User Id=AdvWorks;Password=Oracle1;Data Source=localhost:1521/FREEPDB1;Connection Timeout=60"
              }
            }
            """);

        Assert.True(
            AppSettingsConnectionResolver.TryResolve(
                startupProject,
                Path.Combine(temp.Path),
                out var connectionString,
                out var provider));

        Assert.Contains("FREEPDB1", connectionString, StringComparison.Ordinal);
        Assert.Equal(MyEfVibeProvider.Oracle, provider);
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

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        internal EnvironmentVariableScope(string name, string value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }
}