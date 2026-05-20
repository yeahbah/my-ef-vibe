namespace MyEfVibe;

/// <summary>
/// Distinguishes EF Core query call sites from in-memory LINQ (reflection, HTTP, collections).
/// </summary>
internal static class LinqEfQueryHeuristics
{
    private static readonly string[] EfMarkers =
    [
        "db.",
        "_dbContext.",
        "dbContext.",
        "applicationDbContext.",
        "_applicationDbContext.",
        "appDbContext.",
        "_appDbContext.",
        "DbContext",
        ".Set<",
        "FromSqlRaw(",
        "FromSql(",
        "ExecuteSqlRaw(",
        "ExecuteSql(",
    ];

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

        foreach (var marker in EfMarkers)
        {
            if (normalized.Contains(marker, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
