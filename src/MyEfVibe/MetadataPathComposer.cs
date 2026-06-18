using System.Collections.Immutable;
using Microsoft.CSharp.RuntimeBinder;

namespace MyEfVibe;

internal static class MetadataPathComposer
{
    internal static ImmutableArray<string> Compose(ImmutableHashSet<string> workspaceAssemblyDllPaths)
    {
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assemblyPath in workspaceAssemblyDllPaths)
        {
            discovered.Add(assemblyPath);
        }

        foreach (var platformAssembly in TrustedRuntimeMetadataPaths.Discover())
        {
            discovered.Add(platformAssembly);
        }

        discovered.Add(typeof(ScriptGlobals<>).Assembly.Location);

        discovered.Add(typeof(RuntimeBinderException).Assembly.Location);

        discovered.Add(typeof(Enumerable).Assembly.Location);

        discovered.Add(typeof(Queryable).Assembly.Location);

        discovered.Add(typeof(IQueryable).Assembly.Location);

        AddSharedRuntimeAssembly(discovered, "System.Linq.Queryable.dll");
        AddSharedRuntimeAssembly(discovered, "System.Linq.Expressions.dll");

        return [..discovered];
    }

    private static void AddSharedRuntimeAssembly(HashSet<string> discovered, string assemblyFileName)
    {
        var dotnetRoot = DotNetInstallRoot.Resolve();
        var sharedRoot = Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App");

        if (!Directory.Exists(sharedRoot))
        {
            return;
        }

        foreach (var runtimeFolder in Directory.EnumerateDirectories(sharedRoot).OrderByDescending(static path => path))
        {
            var candidate = Path.Combine(runtimeFolder, assemblyFileName);

            if (!File.Exists(candidate))
            {
                continue;
            }

            discovered.Add(candidate);

            return;
        }
    }
}