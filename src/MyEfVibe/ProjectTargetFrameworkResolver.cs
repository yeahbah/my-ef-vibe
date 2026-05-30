using System.Text.RegularExpressions;

namespace MyEfVibe;

internal static partial class ProjectTargetFrameworkResolver
{
    internal static string ResolveBuildFramework(string csprojAbsolutePath, string? explicitFrameworkOrNull)
    {
        var monikers = CsprojReader.ReadTargetFrameworkMonikers(csprojAbsolutePath);

        if (monikers.Length == 0)
        {
            return NormalizeMoniker(explicitFrameworkOrNull ??
                                    HostRuntimeFramework.PreferredOutputFolderName() ?? "net8.0");
        }

        if (!string.IsNullOrWhiteSpace(explicitFrameworkOrNull))
        {
            var normalized = NormalizeMoniker(explicitFrameworkOrNull);

            var match = monikers.FirstOrDefault(moniker =>
                string.Equals(moniker, normalized, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                throw new WorkspaceException(
                    $"The project does not target `{normalized}`."
                    + $"{Environment.NewLine}Target frameworks in `{Path.GetFileName(csprojAbsolutePath)}`: "
                    + string.Join(", ", monikers));
            }

            return match;
        }

        var hostMoniker = HostRuntimeFramework.PreferredOutputFolderName();

        if (!string.IsNullOrEmpty(hostMoniker)
            && monikers.Any(moniker => string.Equals(moniker, hostMoniker, StringComparison.OrdinalIgnoreCase)))
        {
            return monikers.First(moniker => string.Equals(moniker, hostMoniker, StringComparison.OrdinalIgnoreCase));
        }

        if (monikers.Length == 1)
        {
            return monikers[0];
        }

        return monikers
            .OrderByDescending(static moniker => TfmRankingScore.DescendingScore(
                Path.Combine("dummy", moniker)))
            .First();
    }

    internal static string NormalizeMoniker(string raw)
    {
        var trimmed = raw.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return trimmed;
        }

        if (!trimmed.StartsWith("net", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "net" + trimmed;
        }

        var majorOnly = MajorOnlyMonikerRegex().Match(trimmed);

        if (majorOnly.Success)
        {
            return FormattableString.Invariant($"net{majorOnly.Groups[1].Value}.0");
        }

        return trimmed;
    }

    [GeneratedRegex(@"^net(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex MajorOnlyMonikerRegex();
}