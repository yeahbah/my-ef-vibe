namespace MyEfVibe;

internal static class LinqProjectSourceWalker
{
    internal static IReadOnlyList<string> CollectProjectPaths(string entryCsprojPath)
    {
        var discovered = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(entryCsprojPath);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (!visited.Add(current))
                continue;

            if (CsprojInspector.IsTestProject(current))
                continue;

            discovered.Add(current);

            foreach (var referencePath in CsprojInspector.GetProjectReferencePaths(current))
            {
                if (visited.Contains(referencePath))
                    continue;

                queue.Enqueue(referencePath);
            }
        }

        return discovered;
    }

    internal static IEnumerable<string> EnumerateSourceFiles(string projectDirectory) =>
        Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !IsUnderBuildArtifacts(path));

    internal static bool IsUnderBuildArtifacts(string absolutePath) =>
        absolutePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(static segment =>
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
}
