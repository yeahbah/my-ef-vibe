using System.Text.Json;
using MyEfVibe.Workspace;

namespace MyEfVibe.Tests;

public sealed class WorkspaceDepsManifestNuGetFallbackTests
{
    [Fact]
    public void TryResolve_finds_registered_provider_from_nuget_cache_when_not_in_workspace_deps()
    {
        var packageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            "npgsql.entityframeworkcore.postgresql");

        if (!Directory.Exists(packageRoot))
        {
            return;
        }

        using var tempDirectory = new TempDirectory();
        var entryDll = Path.Combine(tempDirectory.Path, "Test.Workspace.dll");
        File.WriteAllBytes(entryDll, [0x4D, 0x5A]);

        var depsPath = Path.Combine(tempDirectory.Path, "Test.Workspace.deps.json");
        File.WriteAllText(depsPath, BuildSqlServerOnlyDepsJson());

        var manifest = WorkspaceDepsManifest.TryLoad(entryDll);

        Assert.NotNull(manifest);
        manifest!.RegisterDiscoveredProvider(
            ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.Npgsql));

        Assert.True(
            manifest.TryResolve("Npgsql.EntityFrameworkCore.PostgreSQL", out var npgsqlPath));
        Assert.True(File.Exists(npgsqlPath));

        var oracleClientRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            "oracle.manageddataaccess.core");

        if (Directory.Exists(oracleClientRoot))
        {
            manifest.RegisterDiscoveredProvider(
                ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.Oracle));

            Assert.True(
                manifest.TryResolve("Oracle.ManagedDataAccess", out var oraclePath));
            Assert.True(File.Exists(oraclePath));
        }

        var sqliteRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            "microsoft.entityframeworkcore.sqlite.core");

        if (Directory.Exists(sqliteRoot))
        {
            manifest.RegisterDiscoveredProvider(
                ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.Sqlite));

            Assert.True(
                manifest.TryResolve("Microsoft.EntityFrameworkCore.Sqlite", out var sqlitePath));
            Assert.True(File.Exists(sqlitePath));
        }
    }

    [Fact]
    public void TryResolve_does_not_fallback_for_unregistered_provider_assemblies()
    {
        using var tempDirectory = new TempDirectory();
        var entryDll = Path.Combine(tempDirectory.Path, "Test.Workspace.dll");
        File.WriteAllBytes(entryDll, [0x4D, 0x5A]);

        var depsPath = Path.Combine(tempDirectory.Path, "Test.Workspace.deps.json");
        File.WriteAllText(depsPath, BuildSqlServerOnlyDepsJson());

        var manifest = WorkspaceDepsManifest.TryLoad(entryDll);

        Assert.NotNull(manifest);
        Assert.False(manifest!.TryResolve("Npgsql.EntityFrameworkCore.PostgreSQL", out _));
    }

    [Fact]
    public void TryResolve_finds_discovered_firebird_provider_from_nuget_cache_when_registered()
    {
        var packageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            "firebirdsql.entityframeworkcore.firebird");

        if (!Directory.Exists(packageRoot))
        {
            return;
        }

        using var tempDirectory = new TempDirectory();
        var entryDll = Path.Combine(tempDirectory.Path, "Test.Workspace.dll");
        File.WriteAllBytes(entryDll, [0x4D, 0x5A]);

        var depsPath = Path.Combine(tempDirectory.Path, "Test.Workspace.deps.json");
        File.WriteAllText(depsPath, BuildSqlServerOnlyDepsJson());

        var manifest = WorkspaceDepsManifest.TryLoad(entryDll);

        Assert.NotNull(manifest);
        manifest!.RegisterDiscoveredProvider(
            EntityFrameworkProviderCatalog.CreateDescriptor("FirebirdSql.EntityFrameworkCore.Firebird"));

        Assert.True(
            manifest.TryResolve("FirebirdSql.EntityFrameworkCore.Firebird", out var firebirdPath));
        Assert.True(File.Exists(firebirdPath));
    }

    private static string BuildSqlServerOnlyDepsJson()
    {
        var document = new
        {
            runtimeTarget = new { name = ".NETCoreApp,Version=v10.0" },
            targets = new Dictionary<string, object>
            {
                [".NETCoreApp,Version=v10.0"] = new Dictionary<string, object>
                {
                    ["Microsoft.EntityFrameworkCore/10.0.7"] = new
                    {
                        runtime = new Dictionary<string, object>
                        {
                            ["lib/net10.0/Microsoft.EntityFrameworkCore.dll"] = new { }
                        }
                    },
                    ["Microsoft.EntityFrameworkCore.SqlServer/10.0.7"] = new
                    {
                        runtime = new Dictionary<string, object>
                        {
                            ["lib/net10.0/Microsoft.EntityFrameworkCore.SqlServer.dll"] = new { }
                        }
                    }
                }
            },
            libraries = new Dictionary<string, object>
            {
                ["Microsoft.EntityFrameworkCore/10.0.7"] = new
                {
                    type = "package",
                    path = "microsoft.entityframeworkcore/10.0.7"
                },
                ["Microsoft.EntityFrameworkCore.SqlServer/10.0.7"] = new
                {
                    type = "package",
                    path = "microsoft.entityframeworkcore.sqlserver/10.0.7"
                }
            }
        };

        return JsonSerializer.Serialize(document);
    }

    private sealed class TempDirectory : IDisposable
    {
        internal TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "efvibe-tests", Guid.NewGuid().ToString("N"));
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
