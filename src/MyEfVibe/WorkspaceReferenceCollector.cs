using System.Collections.Immutable;

namespace MyEfVibe;

internal static class WorkspaceReferenceCollector
{
    internal static ImmutableHashSet<string> Collect(string outputDirectory, string primaryAssemblyDll)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in ArtifactResolver.CollectReferencePaths(outputDirectory))
            paths.Add(path);

        var manifest = WorkspaceDepsManifest.TryLoad(primaryAssemblyDll);

        if (manifest is not null)
        {
            foreach (var path in manifest.RuntimeAssemblyPaths)
            {
                if (File.Exists(path))
                    paths.Add(path);
            }
        }

        return paths.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
