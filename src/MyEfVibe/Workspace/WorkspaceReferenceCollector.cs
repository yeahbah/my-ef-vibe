using System.Collections.Immutable;

namespace MyEfVibe.Workspace;

internal static class WorkspaceReferenceCollector
{
    internal static ImmutableHashSet<string> Collect(string outputDirectory, string primaryAssemblyDll)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in ArtifactResolver.CollectReferencePaths(outputDirectory))
        {
            paths.Add(path);
        }

        // NuGet paths from .deps.json are metadata references for Roslyn only. Runtime loading uses
        // WorkspaceAssemblyResolver so multiple versions (e.g. DiagnosticSource 9 and 10) are not pinned early.
        var manifest = WorkspaceDepsManifest.TryLoad(primaryAssemblyDll);

        if (manifest is not null)
        {
            foreach (var path in manifest.RuntimeAssemblyPaths)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                if (!WorkspaceAssemblyFilter.ShouldIncludeRoslynMetadata(path))
                {
                    continue;
                }

                paths.Add(path);
            }
        }

        return paths.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }
}