using System.Text.RegularExpressions;

namespace MyEfVibe;

/// <summary>
///     After a snippet has already executed, SQL-fallback probes must not evaluate it again.
///     Tracker mutations (Add/Remove) emit no SQL, and eager DML emits none when database logging
///     is off, so the fallback would otherwise re-run the side effect.
/// </summary>
internal static partial class EvaluationSqlFallbackGuard
{
    internal static bool ShouldSkipProbe(string snippet)
    {
        return !string.IsNullOrWhiteSpace(snippet) && SideEffectingApiRegex().IsMatch(snippet);
    }

    [GeneratedRegex(
        """
        \b(?:
            SaveChanges(?:Async)?|
            ExecuteDelete(?:Async)?|
            ExecuteUpdate(?:Async)?|
            ExecuteSql(?:Raw|Interpolated)?(?:Async)?|
            EnsureDeleted(?:Async)?|
            EnsureCreated(?:Async)?|
            Migrate(?:Async)?|
            Add(?:Range)?|
            Update(?:Range)?|
            Remove(?:Range)?|
            Attach(?:Range)?
        )
        (?:\s*<[^>(]+>)?
        \s*\(
        """,
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant)]
    private static partial Regex SideEffectingApiRegex();
}
