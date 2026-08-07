using System.Text.RegularExpressions;

namespace MyEfVibe;

/// <summary>
///     Detects EF Core APIs that mutate data or schema when evaluated.
///     Deep-scan / translate probes must never evaluate these — they are not deferred IQueryable shapes.
/// </summary>
internal static partial class ProbeMutationGuard
{
    private static readonly Regex MutatingApiPattern = MutatingApiRegex();

    internal static bool ContainsMutatingOperation(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        return MutatingApiPattern.IsMatch(expression);
    }

    internal static string DescribeBlockedOperation() =>
        "Refusing SQL translation probe for mutating EF APIs (ExecuteDelete/Update, ExecuteSql*, SaveChanges, schema changes).";

    [GeneratedRegex(
        """
        \b(?:
            SaveChanges(?:Async)?|
            ExecuteDelete(?:Async)?|
            ExecuteUpdate(?:Async)?|
            ExecuteSql(?:Raw|Interpolated)?(?:Async)?|
            EnsureDeleted(?:Async)?|
            EnsureCreated(?:Async)?|
            Migrate(?:Async)?
        )
        (?:\s*<[^>(]+>)?
        \s*\(
        |
        \bGetService\s*<[^>]*>\s*\(\s*\)\s*\.\s*Delete\s*\(
        """,
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant)]
    private static partial Regex MutatingApiRegex();
}
