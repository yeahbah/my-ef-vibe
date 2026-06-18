namespace MyEfVibe.Linq;

internal static class LinqProjectSourceWalker
{
    /// <summary>
    ///     Projects whose <c>.cs</c> sources are included in <c>:scan lite</c> / <c>:scan deep</c>.
    ///     Includes the EF project graph (<c>-p</c>), the startup graph (<c>-s</c>), and any other projects in the solution
    ///     that reference <c>-p</c>
    ///     (e.g. Application when API → Application → Persistence).
    /// </summary>
    internal static IReadOnlyList<string> CollectScanProjectPaths(
        string efProjectPath,
        string startupProjectPath)
    {
        var merged = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void MergeGraph(string entryCsprojPath)
        {
            foreach (var path in CollectProjectPathsFromEntry(entryCsprojPath))
            {
                if (seen.Add(path))
                {
                    merged.Add(path);
                }
            }
        }

        MergeGraph(efProjectPath);

        if (!string.Equals(efProjectPath, startupProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            MergeGraph(startupProjectPath);
        }

        var solutionDirectory = ProjectReferenceWalker.TryFindSolutionDirectory(efProjectPath)
                                ?? ProjectReferenceWalker.TryFindSolutionDirectory(startupProjectPath);

        if (!string.IsNullOrEmpty(solutionDirectory))
        {
            foreach (var referencer in ProjectReferenceWalker.CollectProjectsReferencing(
                         efProjectPath,
                         solutionDirectory))
            {
                MergeGraph(referencer);
            }
        }

        return merged;
    }

    internal static IReadOnlyList<string> CollectProjectPathsFromEntry(string entryCsprojPath)
    {
        var discovered = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(entryCsprojPath);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (!visited.Add(current))
            {
                continue;
            }

            if (CsprojInspector.IsTestProject(current))
            {
                continue;
            }

            discovered.Add(current);

            foreach (var referencePath in CsprojInspector.GetProjectReferencePaths(current))
            {
                if (visited.Contains(referencePath))
                {
                    continue;
                }

                queue.Enqueue(referencePath);
            }
        }

        return discovered;
    }

    internal static IEnumerable<string> EnumerateSourceFiles(string projectDirectory)
    {
        return Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !IsUnderBuildArtifacts(path));
    }

    internal static bool IsUnderBuildArtifacts(string absolutePath)
    {
        return absolutePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(static segment =>
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }
}