namespace MyEfVibe.Tests;

public sealed class CsprojInspectorProviderTests
{
    [Fact]
    public void TryReadEntityFrameworkProviderDescriptor_discovers_unknown_relational_provider_package()
    {
        using var temp = new TempDirectory();
        var project = Path.Combine(temp.Path, "App.csproj");
        WriteProjectWithProvider(project, "FirebirdSql.EntityFrameworkCore.Firebird");

        var descriptor = CsprojInspector.TryReadEntityFrameworkProviderDescriptor(project);

        Assert.NotNull(descriptor);
        Assert.Equal("FirebirdSql.EntityFrameworkCore.Firebird", descriptor!.PackageId);
        Assert.Null(descriptor.KnownProvider);
        Assert.Null(descriptor.ExtensionMethodName);
    }

    [Fact]
    public void TryReadEntityFrameworkProviderDescriptor_returns_null_for_ef_design_package()
    {
        using var temp = new TempDirectory();
        var project = Path.Combine(temp.Path, "App.csproj");
        WriteProjectWithProvider(project, "Microsoft.EntityFrameworkCore.Design");

        Assert.Null(CsprojInspector.TryReadEntityFrameworkProviderDescriptor(project));
    }

    [Fact]
    public void TryReadEntityFrameworkProvider_reads_sqlserver_package_reference()
    {
        using var temp = new TempDirectory();
        var project = Path.Combine(temp.Path, "AdventureWorks.Infrastructure.Persistence.csproj");
        WriteProjectWithProvider(project, "Microsoft.EntityFrameworkCore.SqlServer");

        var provider = CsprojInspector.TryReadEntityFrameworkProvider(project);

        Assert.Equal(MyEfVibeProvider.SqlServer, provider);
    }

    [Fact]
    public void TryReadEntityFrameworkProvider_reads_mysql_package_reference()
    {
        using var temp = new TempDirectory();
        var project = Path.Combine(temp.Path, "AdventureWorks.Infrastructure.Persistence.csproj");
        WriteProjectWithProvider(project, "MySql.EntityFrameworkCore");

        var provider = CsprojInspector.TryReadEntityFrameworkProvider(project);

        Assert.Equal(MyEfVibeProvider.MySql, provider);
    }

    [Fact]
    public void TryReadEntityFrameworkProvider_reads_oracle_package_reference()
    {
        using var temp = new TempDirectory();
        var project = Path.Combine(temp.Path, "AdventureWorks.Infrastructure.Persistence.csproj");
        WriteProjectWithProvider(project, "Oracle.EntityFrameworkCore");

        var provider = CsprojInspector.TryReadEntityFrameworkProvider(project);

        Assert.Equal(MyEfVibeProvider.Oracle, provider);
    }

    [Fact]
    public void TryReadEntityFrameworkProvider_reads_provider_from_referenced_project()
    {
        using var temp = new TempDirectory();
        var persistence = Path.Combine(temp.Path, "AdventureWorks.Infrastructure.Persistence.csproj");
        WriteProjectWithProvider(persistence, "MySql.EntityFrameworkCore");

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

        var provider = CsprojInspector.TryReadEntityFrameworkProvider(api);

        Assert.Equal(MyEfVibeProvider.MySql, provider);
    }

    [Fact]
    public void TryReadEntityFrameworkProvider_returns_null_when_multiple_providers_are_referenced()
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

        var provider = CsprojInspector.TryReadEntityFrameworkProvider(project);

        Assert.Null(provider);
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
}
