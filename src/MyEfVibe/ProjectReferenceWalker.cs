namespace MyEfVibe;

internal static class ProjectReferenceWalker
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        "node_modules",
        ".git",
        ".vs",
        ".idea",
        "packages",
        "artifacts",
        "TestResults",
    };

    internal static bool ReferencesProject(string csprojPath, string targetProjectPath, int maxDepth = 8)
    {
        var normalizedTarget = Path.GetFullPath(targetProjectPath);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((Path.GetFullPath(csprojPath), 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();

            if (depth > maxDepth)
                continue;

            foreach (var referencePath in CsprojInspector.GetProjectReferencePaths(current))
            {
                var normalizedReference = Path.GetFullPath(referencePath);

                if (string.Equals(normalizedReference, normalizedTarget, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (visited.Add(normalizedReference))
                    queue.Enqueue((normalizedReference, depth + 1));
            }
        }

        return false;
    }

    internal static IReadOnlyList<string> CollectProjectsReferencing(
        string targetProjectPath,
        string searchRootDirectory)
    {
        var normalizedTarget = Path.GetFullPath(targetProjectPath);
        var referencers = new List<string>();

        if (!Directory.Exists(searchRootDirectory))
            return referencers;

        foreach (var candidate in EnumerateProjectFiles(searchRootDirectory))
        {
            if (string.Equals(candidate, normalizedTarget, StringComparison.OrdinalIgnoreCase))
                continue;

            if (ReferencesProject(candidate, normalizedTarget))
                referencers.Add(candidate);
        }

        return referencers;
    }

    internal static IEnumerable<string> EnumerateProjectsReferencing(
        string targetProjectPath,
        params string[] searchRootDirectories)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var searchRoot in searchRootDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(searchRoot) || !Directory.Exists(searchRoot))
                continue;

            foreach (var candidate in CollectProjectsReferencing(targetProjectPath, searchRoot))
            {
                if (seen.Add(candidate))
                    yield return candidate;
            }
        }
    }

    internal static string? TryFindSolutionDirectory(string projectPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath));

        while (!string.IsNullOrEmpty(directory))
        {
            if (Directory.EnumerateFiles(directory, "*.sln").Any())
                return directory;

            directory = Directory.GetParent(directory)?.FullName;
        }

        return null;
    }

    private static IEnumerable<string> EnumerateProjectFiles(string searchRootDirectory)
    {
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(searchRootDirectory));

        while (pending.Count > 0)
        {
            var currentDirectory = pending.Pop();

            IEnumerable<string> childDirectories;

            try
            {
                childDirectories = Directory.EnumerateDirectories(currentDirectory);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var childDirectory in childDirectories)
            {
                var directoryName = Path.GetFileName(childDirectory);

                if (ExcludedDirectoryNames.Contains(directoryName))
                    continue;

                pending.Push(childDirectory);
            }

            IEnumerable<string> projectFiles;

            try
            {
                projectFiles = Directory.EnumerateFiles(currentDirectory, "*.csproj", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var projectFile in projectFiles)
                yield return Path.GetFullPath(projectFile);
        }
    }
}
