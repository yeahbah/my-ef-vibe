namespace MyEfVibe;

/// <summary>
///     Built-in DbContext naming conventions used when project-scoped instance discovery is unavailable (REPL snippets).
///     Scan and deep SQL translation prefer <see cref="DbContextInstanceIdentifierIndex" /> per source file.
/// </summary>
internal static class DbContextQueryMarkers
{
    internal static readonly string[] BuiltInReplaceableIdentifiers =
    [
        "dbContext",
        "_dbContext",
        "DbContext",
        "_context",
        "applicationDbContext",
        "_applicationDbContext",
        "appDbContext",
        "_appDbContext"
    ];

    internal static readonly string[] BuiltInMemberPrefixes =
    [
        "db.",
        "DbContext.",
        "_context.",
        "_dbContext.",
        "dbContext.",
        "applicationDbContext.",
        "_applicationDbContext.",
        "appDbContext.",
        "_appDbContext."
    ];

    internal static readonly string[] TypeNameFragments =
    [
        "DbContext"
    ];
}