namespace MyEfVibe;

/// <summary>
/// Distinguishes EF Core query call sites from in-memory LINQ (reflection, HTTP, collections).
/// </summary>
internal static class LinqEfQueryHeuristics
{
    private static readonly string[] NonEfMarkers =
    [
        "AppDomain.",
        "GetAssemblies(",
        ".GetTypes(",
        "Request.Headers",
        "ManifestModule",
        "Assembly.Load",
        "Directory.",
        "File.",
        "Path.",
        "HttpContext.",
    ];

    internal static bool LooksLikeEfQuery(string statement)
    {
        if (string.IsNullOrWhiteSpace(statement))
            return false;

        var normalized = statement.ReplaceLineEndings(" ");

        foreach (var marker in NonEfMarkers)
        {
            if (normalized.Contains(marker, StringComparison.Ordinal))
                return false;
        }

        foreach (var prefix in DbContextQueryMarkers.MemberPrefixes)
        {
            if (normalized.Contains(prefix, StringComparison.Ordinal))
                return true;
        }

        foreach (var typeName in DbContextQueryMarkers.TypeNameFragments)
        {
            if (normalized.Contains(typeName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
