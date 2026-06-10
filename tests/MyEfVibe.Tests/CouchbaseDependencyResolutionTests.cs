using System.Reflection;

namespace MyEfVibe.Tests;

public sealed class CouchbaseDependencyResolutionTests
{
    [Fact]
    public void Couchbase_provider_includes_object_pool_and_netclient_assemblies()
    {
        var assemblies = ProviderAssemblyNames.For(MyEfVibeProvider.Couchbase).ToArray();

        Assert.Contains("Couchbase.EntityFrameworkCore", assemblies, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Couchbase.NetClient", assemblies, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Microsoft.Extensions.ObjectPool", assemblies, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryResolve_ObjectPool_from_nuget_after_couchbase_provider_registered()
    {
        var persistenceDll = FindCouchbasePersistenceDll();

        if (persistenceDll is null)
        {
            return;
        }

        var manifest = WorkspaceDepsManifest.TryLoad(persistenceDll);

        Assert.NotNull(manifest);

        manifest!.RegisterDiscoveredProvider(ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.Couchbase));

        var requested = new AssemblyName(
            "Microsoft.Extensions.ObjectPool, Version=6.0.0.0, Culture=neutral, PublicKeyToken=adb9793829ddae60");

        Assert.True(manifest.TryResolve(requested, true, out var path));
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(File.Exists(path));
    }

    private static string? FindCouchbasePersistenceDll()
    {
        var candidates = new[]
        {
            "/home/yeahbah/Projects/AdventureWorksCouchBase/apps/api-dotnet/src/AdventureWorks.Infrastructure.Persistence/bin/Debug/net10.0/AdventureWorks.Infrastructure.Persistence.dll",
            "/home/yeahbah/Projects/AdventureWorksCouchBase/apps/api-dotnet/src/AdventureWorks.Infrastructure.Persistence/bin/Release/net10.0/AdventureWorks.Infrastructure.Persistence.dll"
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
