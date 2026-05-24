using System.Text.RegularExpressions;

namespace MyEfVibe;

/// <summary>
/// Removes EF operators that do not affect translated SQL from deep-scan probe expressions.
/// </summary>
internal static class EfProbeExpressionSanitizer
{
    private static readonly string[] TranslationNeutralOperators =
    [
        "AsNoTracking",
        "AsTracking",
        "AsNoTrackingWithIdentityResolution",
        "AsSplitQuery",
        "AsSingleQuery",
        "IgnoreQueryFilters",
        "IgnoreAutoIncludes",
    ];

    internal static string RemoveTranslationNeutralOperators(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return expression;

        var result = expression;

        foreach (var operatorName in TranslationNeutralOperators)
        {
            var pattern = $@"\.(?:\s*)?{Regex.Escape(operatorName)}\s*\(\s*\)";

            result = Regex.Replace(result, pattern, string.Empty, RegexOptions.CultureInvariant);
        }

        while (result.Contains("..", StringComparison.Ordinal))
            result = result.Replace("..", ".", StringComparison.Ordinal);

        result = Regex.Replace(result, @"\s+\.", ".", RegexOptions.CultureInvariant);

        return result.Trim();
    }
}
