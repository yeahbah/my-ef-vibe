using System.Reflection;
using System.Runtime.Loader;

namespace MyEfVibe.Tests;

public sealed class WorkspaceDepsManifestProviderOnlyFallbackTests
{
    [Fact]
    public void TryResolve_does_not_fallback_for_non_provider_assemblies()
    {
        var packageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            "system.configuration.configurationmanager");

        if (!Directory.Exists(packageRoot))
            return;

        using var tempDirectory = new TempDirectory();
        var entryDll = Path.Combine(tempDirectory.Path, "Test.Workspace.dll");
        File.WriteAllBytes(entryDll, [0x4D, 0x5A]);

        var depsPath = Path.Combine(tempDirectory.Path, "Test.Workspace.deps.json");
        File.WriteAllText(depsPath, BuildSqlServerOnlyDepsJson());

        var manifest = WorkspaceDepsManifest.TryLoad(entryDll);

        Assert.NotNull(manifest);
        Assert.False(
            manifest!.TryResolve("System.Configuration.ConfigurationManager", out _));
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
                Directory.Delete(Path, recursive: true);
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
