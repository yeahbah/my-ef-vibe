namespace MyEfVibe.Tests;

public sealed class AppSettingsConnectionResolverTests
{
    [Fact]
    public void TryResolve_adventureworks_mysql_appsettings()
    {
        using var temp = new TempDirectory();
        var efProject = Path.Combine(temp.Path, "AdventureWorks.Infrastructure.Persistence.csproj");
        WriteProjectWithProvider(efProject, "MySql.EntityFrameworkCore");
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

        Assert.True(
            AppSettingsConnectionResolver.TryResolve(
                startupProject,
                efProject,
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
        var efProject = Path.Combine(temp.Path, "AdventureWorks.Infrastructure.Persistence.csproj");
        WriteProjectWithProvider(efProject, "Oracle.EntityFrameworkCore");
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
                efProject,
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
        var efProject = Path.Combine(temp.Path, "AdventureWorks.Infrastructure.Persistence.csproj");
        WriteProjectWithProvider(efProject, "Microsoft.EntityFrameworkCore.SqlServer");
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
                efProject,
                Path.Combine(temp.Path),
                out var connectionString,
                out var provider));

        Assert.Contains("FREEPDB1", connectionString, StringComparison.Ordinal);
        Assert.Equal(MyEfVibeProvider.Oracle, provider);
    }

    [Fact]
    public void ResolveProvider_prefers_appsettings_override_over_ef_project_package()
    {
        using var temp = new TempDirectory();
        var efProject = Path.Combine(temp.Path, "AdventureWorks.Infrastructure.Persistence.csproj");
        WriteProjectWithProvider(efProject, "Microsoft.EntityFrameworkCore.SqlServer");
        var startupProject = Path.Combine(temp.Path, "AdventureWorks.API.csproj");
        File.WriteAllText(startupProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(
            Path.Combine(temp.Path, "appsettings.json"),
            """
            {
              "EntityFrameworkCoreSettings": { "DatabaseProvider": "PostgreSQL" }
            }
            """);

        var provider = AppSettingsConnectionResolver.ResolveProvider(startupProject, efProject);

        Assert.Equal(MyEfVibeProvider.Npgsql, provider);
    }

    private static void WriteProjectWithProvider(string csprojPath, string packageId)
    {
        File.WriteAllText(
            csprojPath,
            $$"""
              <Project Sdk="Microsoft.NET.Sdk">
                <ItemGroup>
                  <PackageReference Include="{{packageId}}" Version="10.0.7" />
                </ItemGroup>
              </Project>
              """);
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
