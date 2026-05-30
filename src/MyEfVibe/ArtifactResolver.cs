namespace MyEfVibe;

internal static class ArtifactResolver
{
    internal static IReadOnlyList<string> CollectReferencePaths(string workspaceOutputDirectory)
    {
        if (!Directory.Exists(workspaceOutputDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(workspaceOutputDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .Where(static path => !IsHostToolBinary(path))
            .Select(static path => Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsHostToolBinary(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);

        return string.Equals(fileName, "myefvibe", StringComparison.OrdinalIgnoreCase);
    }
}