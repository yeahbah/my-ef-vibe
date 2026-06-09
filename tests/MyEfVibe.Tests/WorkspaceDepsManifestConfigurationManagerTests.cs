namespace MyEfVibe.Tests;

public sealed class WorkspaceDepsManifestConfigurationManagerTests
{
    [Fact]
    public void TryResolveConfigurationManagerForHost_prefers_compatible_net_lib_for_net10_workspace()
    {
        var packageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            "system.configuration.configurationmanager",
            "9.0.4");

        if (!Directory.Exists(packageRoot))
        {
            return;
        }

        using var tempDirectory = new TempDirectory();
        var entryDll = Path.Combine(tempDirectory.Path, "Test.Workspace.dll");
        File.WriteAllBytes(entryDll, [0x4D, 0x5A]);

        var depsPath = Path.Combine(tempDirectory.Path, "Test.Workspace.deps.json");
        File.WriteAllText(depsPath, BuildConfigurationManagerDepsJson());

        var manifest = WorkspaceDepsManifest.TryLoad(entryDll);

        Assert.NotNull(manifest);
        var candidates = manifest!.EnumerateConfigurationManagerHostCandidates().ToArray();

        Assert.NotEmpty(candidates);
        Assert.Contains(
            $"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}net9.0{Path.DirectorySeparatorChar}",
            candidates[0],
            StringComparison.OrdinalIgnoreCase);
        Assert.True(manifest.TryResolveConfigurationManagerForHost(out var path));
        Assert.Equal(candidates[0], path);
    }

    private static string BuildConfigurationManagerDepsJson()
    {
        return """
               {
                 "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0" },
                 "targets": {
                   ".NETCoreApp,Version=v10.0": {
                     "System.Configuration.ConfigurationManager/9.0.4": {
                       "runtime": {
                         "lib/net9.0/System.Configuration.ConfigurationManager.dll": {}
                       }
                     }
                   }
                 },
                 "libraries": {
                   "System.Configuration.ConfigurationManager/9.0.4": {
                     "type": "package",
                     "path": "system.configuration.configurationmanager/9.0.4"
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
