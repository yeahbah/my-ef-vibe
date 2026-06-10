namespace MyEfVibe.Tests;

public sealed class CouchbaseEntityFrameworkCompatibilityTests
{
    [Fact]
    public void TryValidateEfCoreVersion_rejects_ef_core_10_couchbase_project()
    {
        var csproj = WriteCsproj(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Couchbase.EntityFrameworkCore" Version="1.0.0" />
                <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.7" />
              </ItemGroup>
            </Project>
            """);

        Assert.False(CouchbaseEntityFrameworkCompatibility.TryValidateEfCoreVersion(csproj, out var error));
        Assert.NotNull(error);
        Assert.Contains("EF Core 8", error, StringComparison.Ordinal);
        Assert.Contains("10.0", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateEfCoreVersion_allows_ef_core_8_couchbase_project()
    {
        var csproj = WriteCsproj(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Couchbase.EntityFrameworkCore" Version="1.0.0" />
                <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.14" />
              </ItemGroup>
            </Project>
            """);

        Assert.True(CouchbaseEntityFrameworkCompatibility.TryValidateEfCoreVersion(csproj, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void TryExplainTypeLoadFailure_maps_lock_release_behavior_to_couchbase_hint()
    {
        const string message =
            "Method 'get_LockReleaseBehavior' in type 'Couchbase.EntityFrameworkCore.Migrations.Internal.CouchbaseHistoryRepository'";

        var hint = CouchbaseEntityFrameworkCompatibility.TryExplainTypeLoadFailure(message);

        Assert.NotNull(hint);
        Assert.Contains("EF Core 8", hint, StringComparison.Ordinal);
    }

    private static string WriteCsproj(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "efvibe-tests", Guid.NewGuid().ToString("N"), "Sample.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        return path;
    }
}
