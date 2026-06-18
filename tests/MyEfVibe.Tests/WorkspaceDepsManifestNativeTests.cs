using System.Text.Json;
using MyEfVibe.Workspace;

namespace MyEfVibe.Tests;

public sealed class WorkspaceDepsManifestNativeTests
{
    [Fact]
    public void TryResolveNativeLibrary_prefers_host_rid_for_sqlite()
    {
        var rid = HostRuntimeIdentifier.GetRuntimeFallbacks().First();
        var (nativeFileName, relativeNativePath) = GetNativeAssetForRid(rid);

        var packageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            "sqlitepclraw.lib.e_sqlite3",
            "2.1.11",
            relativeNativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(packageRoot))
        {
            return;
        }

        using var tempDirectory = new TempDirectory();
        var entryDll = Path.Combine(tempDirectory.Path, "Test.Workspace.dll");
        File.WriteAllBytes(entryDll, [0x4D, 0x5A]);

        var depsPath = Path.Combine(tempDirectory.Path, "Test.Workspace.deps.json");
        File.WriteAllText(depsPath, BuildDepsJson(rid, relativeNativePath));

        var manifest = WorkspaceDepsManifest.TryLoad(entryDll);

        Assert.NotNull(manifest);
        Assert.True(manifest!.TryResolveNativeLibrary(out var nativePath, nativeFileName));
        Assert.Equal(packageRoot, nativePath);
    }

    private static (string NativeFileName, string RelativeNativePath) GetNativeAssetForRid(string rid)
    {
        if (rid.StartsWith("osx", StringComparison.OrdinalIgnoreCase))
        {
            return ("libe_sqlite3.dylib", $"runtimes/{rid}/native/libe_sqlite3.dylib");
        }

        if (rid.StartsWith("linux", StringComparison.OrdinalIgnoreCase))
        {
            return ("libe_sqlite3.so", $"runtimes/{rid}/native/libe_sqlite3.so");
        }

        if (rid.StartsWith("win", StringComparison.OrdinalIgnoreCase))
        {
            return ("e_sqlite3.dll", $"runtimes/{rid}/native/e_sqlite3.dll");
        }

        return ("libe_sqlite3.so", "runtimes/linux-x64/native/libe_sqlite3.so");
    }

    private static string BuildDepsJson(string rid, string relativeNativePath)
    {
        var document = new
        {
            runtimeTarget = new { name = ".NETCoreApp,Version=v10.0" },
            targets = new Dictionary<string, object>
            {
                [".NETCoreApp,Version=v10.0"] = new Dictionary<string, object>
                {
                    ["SQLitePCLRaw.lib.e_sqlite3/2.1.11"] = new
                    {
                        runtimeTargets = new Dictionary<string, object>
                        {
                            [relativeNativePath] = new { rid, assetType = "native" }
                        }
                    }
                }
            },
            libraries = new Dictionary<string, object>
            {
                ["SQLitePCLRaw.lib.e_sqlite3/2.1.11"] = new
                {
                    type = "package",
                    path = "sqlitepclraw.lib.e_sqlite3/2.1.11"
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