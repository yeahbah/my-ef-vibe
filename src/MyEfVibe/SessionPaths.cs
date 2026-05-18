namespace MyEfVibe;

internal static class SessionPaths
{
    internal const string DefaultWorkspaceFolderName = "efvibe";

    internal static string GetDefaultWorkspaceDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                DefaultWorkspaceFolderName);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            $".{DefaultWorkspaceFolderName}");
    }

    internal static string EnsureSessionDirectory(string sessionDirectory)
    {
        var normalized = Path.GetFullPath(sessionDirectory.TrimEnd(Path.DirectorySeparatorChar));

        Directory.CreateDirectory(normalized);

        return normalized;
    }

    internal static string GetDbContextSessionFolderName(string dbContextName)
    {
        return SanitizeFolderName(string.IsNullOrWhiteSpace(dbContextName) ? "DbContext" : dbContextName);
    }

    internal static string EnsureDbContextSessionDirectory(string workspaceRoot, string dbContextName)
    {
        var contextFolder = GetDbContextSessionFolderName(dbContextName);

        return EnsureSessionDirectory(Path.Combine(workspaceRoot, contextFolder));
    }

    internal static string EnsurePendingSessionDirectory(string workspaceRoot) =>
        EnsureSessionDirectory(Path.Combine(workspaceRoot, ".pending"));

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(name.Length);

        foreach (var character in name)
            builder.Append(invalid.Contains(character) ? '_' : character);

        var sanitized = builder.ToString().Trim();

        return sanitized.Length == 0 ? "project" : sanitized;
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
