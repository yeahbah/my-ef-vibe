namespace MyEfVibe;

internal static class SessionPaths
{
    internal static string EnsureSessionDirectory(string sessionDirectory)
    {
        var normalized = Path.GetFullPath(sessionDirectory.TrimEnd(Path.DirectorySeparatorChar));

        Directory.CreateDirectory(normalized);

        return normalized;
    }

    internal static string ResolveExportPath(string sessionDirectory, string? pathOrNull, string format)
    {
        var extension = format.Equals("json", StringComparison.OrdinalIgnoreCase) ? "json" : "csv";

        if (string.IsNullOrWhiteSpace(pathOrNull))
            return Path.Combine(sessionDirectory, $"myefvibe-export-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}");

        return Path.IsPathRooted(pathOrNull)
            ? pathOrNull
            : Path.Combine(sessionDirectory, pathOrNull);
    }
}
