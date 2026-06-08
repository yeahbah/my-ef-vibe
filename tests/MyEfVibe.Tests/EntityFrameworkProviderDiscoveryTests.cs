namespace MyEfVibe.Tests;

public sealed class EntityFrameworkProviderDiscoveryTests
{
    public static TheoryData<string, string, string?> KnownProviderGoldenData { get; } = new()
    {
        { "Microsoft.EntityFrameworkCore.SqlServer", "UseSqlServer", "sqlserver" },
        { "Npgsql.EntityFrameworkCore.PostgreSQL", "UseNpgsql", "npgsql" },
        { "Microsoft.EntityFrameworkCore.Sqlite", "UseSqlite", "sqlite" },
        { "Oracle.EntityFrameworkCore", "UseOracle", "oracle" },
        { "Pomelo.EntityFrameworkCore.MySql", "UseMySql", "mysql" },
        { "MariaDB.EntityFrameworkCore", "UseMySQL", "mariadb" },
        { "MySql.EntityFrameworkCore", "UseMySQL", null }
    };

    [Theory]
    [MemberData(nameof(KnownProviderGoldenData))]
    public void TryDiscoverFromProject_returns_golden_descriptor_for_known_package(
        string packageId,
        string expectedExtensionMethod,
        string? expectedAlias)
    {
        using var temp = new TempDirectory();
        var project = Path.Combine(temp.Path, "App.csproj");
        CsprojTestHelper.WriteProjectWithProvider(project, packageId);

        var descriptor = EntityFrameworkProviderDiscovery.TryDiscoverFromProject(project);

        Assert.NotNull(descriptor);
        Assert.Equal(packageId, descriptor!.PackageId);
        Assert.Equal(packageId, descriptor.ProviderAssemblyName);
        Assert.Equal(expectedExtensionMethod, descriptor.ExtensionMethodName);
        Assert.NotNull(descriptor.KnownProvider);

        if (expectedAlias is not null)
        {
            Assert.Equal(
                ProviderParser.ParseDescriptorOrNull(expectedAlias)!.PackageId,
                descriptor.PackageId);
        }
    }

    [Fact]
    public void TryDiscoverFromProject_returns_generic_descriptor_for_firebird()
    {
        using var temp = new TempDirectory();
        var project = Path.Combine(temp.Path, "App.csproj");
        CsprojTestHelper.WriteProjectWithProvider(project, "FirebirdSql.EntityFrameworkCore.Firebird");

        var descriptor = EntityFrameworkProviderDiscovery.TryDiscoverFromProject(project);

        Assert.NotNull(descriptor);
        Assert.Equal("FirebirdSql.EntityFrameworkCore.Firebird", descriptor!.PackageId);
        Assert.Null(descriptor.KnownProvider);
        Assert.Null(descriptor.ExtensionMethodName);
    }

    [Fact]
    public void TryDiscoverFromProject_returns_null_when_multiple_providers_are_referenced()
    {
        using var temp = new TempDirectory();
        var project = Path.Combine(temp.Path, "Broken.csproj");
        File.WriteAllText(
            project,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.7" />
                <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
              </ItemGroup>
            </Project>
            """);

        Assert.Null(EntityFrameworkProviderDiscovery.TryDiscoverFromProject(project));
    }

    [Fact]
    public void TryDescribeAmbiguousProviders_lists_conflicting_packages()
    {
        using var temp = new TempDirectory();
        var project = Path.Combine(temp.Path, "Broken.csproj");
        File.WriteAllText(
            project,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.7" />
                <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
              </ItemGroup>
            </Project>
            """);

        Assert.True(EntityFrameworkProviderDiscovery.TryDescribeAmbiguousProviders(project, out var message));

        Assert.NotNull(message);
        Assert.Contains("Microsoft.EntityFrameworkCore.SqlServer", message, StringComparison.Ordinal);
        Assert.Contains("Npgsql.EntityFrameworkCore.PostgreSQL", message, StringComparison.Ordinal);
        Assert.Contains("--provider", message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryDiscoverFromProject_finds_provider_in_referenced_project()
    {
        using var temp = new TempDirectory();
        var persistence = Path.Combine(temp.Path, "AdventureWorks.Infrastructure.Persistence.csproj");
        CsprojTestHelper.WriteProjectWithProvider(persistence, "Npgsql.EntityFrameworkCore.PostgreSQL");

        var api = Path.Combine(temp.Path, "AdventureWorks.API.csproj");
        File.WriteAllText(
            api,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="AdventureWorks.Infrastructure.Persistence.csproj" />
              </ItemGroup>
            </Project>
            """);

        var descriptor = EntityFrameworkProviderDiscovery.TryDiscoverFromProject(api);

        Assert.NotNull(descriptor);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", descriptor!.PackageId);
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

internal static class CsprojTestHelper
{
    internal static void WriteProjectWithProvider(string csprojPath, string packageId)
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
}
