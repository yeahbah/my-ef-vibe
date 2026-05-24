namespace MyEfVibe;

/// <summary>
/// Common field/property names for an injected EF <see cref="Microsoft.EntityFrameworkCore.DbContext"/>.
/// Used by scan, probes, and repository-snippet adaptation.
/// </summary>
internal static class DbContextQueryMarkers
{
    internal static readonly string[] MemberPrefixes =
    [
        "db.",
        "DbContext.",
        "_context.",
        "_dbContext.",
        "dbContext.",
        "applicationDbContext.",
        "_applicationDbContext.",
        "appDbContext.",
        "_appDbContext.",
    ];

    internal static readonly string[] TypeNameFragments =
    [
        "DbContext",
    ];
}
