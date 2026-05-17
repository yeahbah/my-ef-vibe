namespace MyEfVibe;

internal static class ProjectPathResolver
{
    internal static FileInfo ResolveCsproj(string explicitCandidate, string searchDirectory)
    {
        foreach (var candidatePath in EnumerateCandidatePaths(explicitCandidate, searchDirectory))
        {
            if (!File.Exists(candidatePath))
                continue;

            if (!string.Equals(Path.GetExtension(candidatePath), ".csproj", StringComparison.OrdinalIgnoreCase))
                throw new WorkspaceException("Project paths must resolve to a `.csproj` file.");

            return new FileInfo(candidatePath);
        }

        throw new WorkspaceException($"Specified project `{explicitCandidate}` does not exist.");
    }

    internal static string ResolveSearchDirectory(
        string sessionDirectory,
        string? projectPathOrNull,
        string? startupProjectPathOrNull)
    {
        foreach (var candidate in new[] { startupProjectPathOrNull, projectPathOrNull })
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var resolved = TryResolveExistingPath(candidate, sessionDirectory);

            if (resolved is null)
                continue;

            var directory = Path.GetDirectoryName(resolved);

            if (!string.IsNullOrEmpty(directory))
                return directory;
        }

        return Environment.CurrentDirectory;
    }

    private static IEnumerable<string> EnumerateCandidatePaths(string explicitCandidate, string searchDirectory)
    {
        var trimmed = explicitCandidate.Trim().TrimEnd(Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(trimmed))
        {
            yield return Path.GetFullPath(trimmed);
            yield break;
        }

        yield return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, trimmed));
        yield return Path.GetFullPath(Path.Combine(searchDirectory, trimmed));
    }

    private static string? TryResolveExistingPath(string pathCandidate, string searchDirectory)
    {
        foreach (var candidate in EnumerateCandidatePaths(pathCandidate, searchDirectory))
        {
            if (File.Exists(candidate))
                return candidate;

            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
