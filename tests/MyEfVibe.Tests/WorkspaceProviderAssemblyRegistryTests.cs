using MyEfVibe.Workspace;

namespace MyEfVibe.Tests;

public sealed class WorkspaceProviderAssemblyRegistryTests
{
    [Fact]
    public void Register_allows_nuget_fallback_for_discovered_provider_package()
    {
        using var tempDirectory = new TempDirectory();
        var entryDll = Path.Combine(tempDirectory.Path, "Test.Workspace.dll");
        File.WriteAllBytes(entryDll, [0x4D, 0x5A]);

        var depsPath = Path.Combine(tempDirectory.Path, "Test.Workspace.deps.json");
        File.WriteAllText(depsPath, BuildMinimalDepsJson());

        var manifest = WorkspaceDepsManifest.TryLoad(entryDll);

        Assert.NotNull(manifest);
        Assert.False(manifest!.TryResolve("FirebirdSql.EntityFrameworkCore.Firebird", out _));

        manifest.RegisterDiscoveredProvider(
            EntityFrameworkProviderCatalog.CreateDescriptor("FirebirdSql.EntityFrameworkCore.Firebird"));

        var packageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            "firebirdsql.entityframeworkcore.firebird");

        if (!Directory.Exists(packageRoot))
        {
            return;
        }

        Assert.True(
            manifest.TryResolve("FirebirdSql.EntityFrameworkCore.Firebird", out var path));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Register_known_sqlserver_provider_includes_sqlclient_dependency()
    {
        using var tempDirectory = new TempDirectory();
        var entryDll = Path.Combine(tempDirectory.Path, "Test.Workspace.dll");
        File.WriteAllBytes(entryDll, [0x4D, 0x5A]);

        var depsPath = Path.Combine(tempDirectory.Path, "Test.Workspace.deps.json");
        File.WriteAllText(depsPath, BuildMinimalDepsJson());

        var manifest = WorkspaceDepsManifest.TryLoad(entryDll);

        Assert.NotNull(manifest);
        manifest!.RegisterDiscoveredProvider(
            ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.SqlServer));

        var sqlClientRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            "microsoft.data.sqlclient");

        if (!Directory.Exists(sqlClientRoot))
        {
            return;
        }

        Assert.True(manifest.TryResolve("Microsoft.Data.SqlClient", out var path));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Register_sqlite_provider_includes_sqlitepcl_batteries_dependency()
    {
        var batteriesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            ProviderAssemblyNames.GetNuGetPackageFolderName("SQLitePCLRaw.batteries_v2"));

        if (!Directory.Exists(batteriesRoot))
        {
            return;
        }

        using var tempDirectory = new TempDirectory();
        var entryDll = Path.Combine(tempDirectory.Path, "Test.Workspace.dll");
        File.WriteAllBytes(entryDll, [0x4D, 0x5A]);

        var depsPath = Path.Combine(tempDirectory.Path, "Test.Workspace.deps.json");
        File.WriteAllText(depsPath, BuildMinimalDepsJson());

        var manifest = WorkspaceDepsManifest.TryLoad(entryDll);

        Assert.NotNull(manifest);
        manifest!.RegisterDiscoveredProvider(
            ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.Sqlite));

        Assert.True(
            manifest.TryResolve("SQLitePCLRaw.batteries_v2", out var path),
            "Expected NuGet fallback for SQLitePCLRaw.batteries_v2");
        Assert.True(File.Exists(path));
    }

    private static string BuildMinimalDepsJson()
    {
        return """
               {
                 "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0" },
                 "targets": {
                   ".NETCoreApp,Version=v10.0": {
                     "Microsoft.EntityFrameworkCore/10.0.7": {
                       "runtime": {
                         "lib/net10.0/Microsoft.EntityFrameworkCore.dll": {}
                       }
                     }
                   }
                 },
                 "libraries": {
                   "Microsoft.EntityFrameworkCore/10.0.7": {
                     "type": "package",
                     "path": "microsoft.entityframeworkcore/10.0.7"
                   }
                 }
               }
               """;
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
