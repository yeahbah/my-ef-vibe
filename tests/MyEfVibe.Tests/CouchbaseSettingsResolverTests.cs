namespace MyEfVibe.Tests;

public sealed class CouchbaseSettingsResolverTests
{
    [Fact]
    public void TryResolve_reads_couchbase_section_from_appsettings()
    {
        using var temp = new TempDirectory();
        var startupProject = Path.Combine(temp.Path, "AdventureWorks.API.csproj");
        File.WriteAllText(startupProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(
            Path.Combine(temp.Path, "appsettings.json"),
            """
            {
              "Couchbase": {
                "ConnectionString": "couchbase://localhost",
                "Username": "Administrator",
                "Password": "secret",
                "BucketName": "adventureworks",
                "ScopeName": "aw",
                "CollectionName": "entities"
              }
            }
            """);

        Assert.True(CouchbaseSettingsResolver.TryResolve(startupProject, out var settings));
        Assert.Equal("couchbase://localhost", settings.ConnectionString);
        Assert.Equal("Administrator", settings.Username);
        Assert.Equal("secret", settings.Password);
        Assert.Equal("adventureworks", settings.BucketName);
        Assert.Equal("aw", settings.ScopeName);
        Assert.Equal("entities", settings.CollectionName);
    }

    [Fact]
    public void TryResolve_reads_legacy_default_connection_object()
    {
        using var temp = new TempDirectory();
        var startupProject = Path.Combine(temp.Path, "AdventureWorks.API.csproj");
        File.WriteAllText(startupProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(
            Path.Combine(temp.Path, "appsettings.json"),
            """
            {
              "DefaultConnection": {
                "ConnectionString": "couchbase://127.0.0.1",
                "Username": "admin",
                "Password": "pw",
                "BucketName": "bucket",
                "ScopeName": "scope"
              }
            }
            """);

        Assert.True(CouchbaseSettingsResolver.TryResolve(startupProject, out var settings));
        Assert.Equal("couchbase://127.0.0.1", settings.ConnectionString);
        Assert.Equal("admin", settings.Username);
        Assert.Equal("bucket", settings.BucketName);
        Assert.Equal("scope", settings.ScopeName);
    }

    [Fact]
    public void TryResolve_reads_flat_user_secrets_keys()
    {
        using var temp = new TempDirectory();
        var startupProject = Path.Combine(temp.Path, "AdventureWorks.API.csproj");
        File.WriteAllText(
            startupProject,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <UserSecretsId>aw-couchbase-test</UserSecretsId>
              </PropertyGroup>
            </Project>
            """);

        var secretsRoot = UserSecretsConnectionResolver.GetUserSecretsRootDirectory();
        Assert.False(string.IsNullOrWhiteSpace(secretsRoot));

        var secretsDirectory = Path.Combine(secretsRoot!, "aw-couchbase-test");
        Directory.CreateDirectory(secretsDirectory);
        File.WriteAllText(
            Path.Combine(secretsDirectory, "secrets.json"),
            """
            {
              "Couchbase:ConnectionString": "couchbases://cb.example.com",
              "Couchbase:Username": "cb-user",
              "Couchbase:Password": "cb-pass",
              "Couchbase:BucketName": "prod",
              "Couchbase:ScopeName": "tenant"
            }
            """);

        try
        {
            Assert.True(CouchbaseSettingsResolver.TryResolve(startupProject, out var settings));
            Assert.Equal("couchbases://cb.example.com", settings.ConnectionString);
            Assert.Equal("cb-user", settings.Username);
            Assert.Equal("prod", settings.BucketName);
        }
        finally
        {
            try
            {
                Directory.Delete(secretsDirectory, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public void TryResolve_fails_when_required_fields_missing()
    {
        using var temp = new TempDirectory();
        var startupProject = Path.Combine(temp.Path, "AdventureWorks.API.csproj");
        File.WriteAllText(startupProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(
            Path.Combine(temp.Path, "appsettings.json"),
            """
            {
              "Couchbase": {
                "ConnectionString": "couchbase://localhost",
                "Username": "Administrator"
              }
            }
            """);

        Assert.False(CouchbaseSettingsResolver.TryResolve(startupProject, out _));
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
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
