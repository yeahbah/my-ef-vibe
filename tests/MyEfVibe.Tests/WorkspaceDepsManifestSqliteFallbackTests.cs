namespace MyEfVibe.Tests;

public sealed class WorkspaceDepsManifestSqliteFallbackTests
{
    [Fact]
    public void TryResolve_finds_sqlitepcl_assemblies_from_nuget_cache()
    {
        foreach (var assemblyName in new[]
                 {
                     "Microsoft.EntityFrameworkCore.Sqlite",
                     "Microsoft.Data.Sqlite",
                     "SQLitePCLRaw.core",
                     "SQLitePCLRaw.provider.e_sqlite3",
                     "SQLitePCLRaw.batteries_v2"
                 })
        {
            var packageFolder = ProviderAssemblyNames.GetNuGetPackageFolderName(assemblyName);
            var packageRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages",
                packageFolder);

            if (!Directory.Exists(packageRoot))
            {
                return;
            }
        }

        using var tempDirectory = new TempDirectory();
        var entryDll = Path.Combine(tempDirectory.Path, "Test.Workspace.dll");
        File.WriteAllBytes(entryDll, [0x4D, 0x5A]);

        var depsPath = Path.Combine(tempDirectory.Path, "Test.Workspace.deps.json");
        File.WriteAllText(depsPath, BuildSqlServerOnlyDepsJson());

        var manifest = WorkspaceDepsManifest.TryLoad(entryDll);

        Assert.NotNull(manifest);
        manifest!.RegisterDiscoveredProvider(
            ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.Sqlite));

        foreach (var assemblyName in new[]
                 {
                     "Microsoft.EntityFrameworkCore.Sqlite",
                     "SQLitePCLRaw.core",
                     "SQLitePCLRaw.provider.e_sqlite3",
                     "SQLitePCLRaw.batteries_v2"
                 })
        {
            Assert.True(
                manifest!.TryResolve(assemblyName, out var path),
                $"Expected NuGet fallback for {assemblyName}");
            Assert.True(File.Exists(path));
        }

        var dataSqliteReference = AssemblyReferenceReader.Read(
                manifest!.TryResolve("Microsoft.Data.Sqlite", out var dataSqlitePath)
                    ? dataSqlitePath
                    : string.Empty)
            .First(reference => string.Equals(reference.Name, "SQLitePCLRaw.core", StringComparison.OrdinalIgnoreCase));

        Assert.True(
            manifest.TryResolve(dataSqliteReference, out var corePath),
            $"Expected versioned NuGet fallback for {dataSqliteReference}");
        Assert.True(File.Exists(corePath));

        Assert.True(
            manifest.TryResolve("SQLitePCLRaw.batteries_v2", out var batteriesPath),
            "Expected NuGet fallback for SQLitePCLRaw.batteries_v2");
        Assert.Contains($"{Path.DirectorySeparatorChar}netstandard2.0{Path.DirectorySeparatorChar}", batteriesPath,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSqlServerOnlyDepsJson()
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