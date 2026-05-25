using System;
using System.IO;

namespace MyEfVibe.VisualStudio.Services;

internal static class PathResolver
{
    internal static string ResolvePath(string value, string solutionDirectory)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            return string.Empty;

        var expanded = trimmed
            .Replace("$(SolutionDir)", EnsureTrailingSeparator(solutionDirectory))
            .Replace("$(solutionDir)", EnsureTrailingSeparator(solutionDirectory))
            .Replace("${workspaceFolder}", solutionDirectory);

        expanded = Environment.ExpandEnvironmentVariables(expanded);

        if (Path.IsPathRooted(expanded) || string.IsNullOrWhiteSpace(solutionDirectory))
            return Path.GetFullPath(expanded);

        return Path.GetFullPath(Path.Combine(solutionDirectory, expanded));
    }

    private static string EnsureTrailingSeparator(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return string.Empty;

        return directory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? directory
            : directory + Path.DirectorySeparatorChar;
    }
}
