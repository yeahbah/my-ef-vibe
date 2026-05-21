namespace MyEfVibe;

internal sealed class DbContextScanScope
{
    internal DbContextScanScope(
        string selectedContextTypeName,
        IReadOnlySet<string> otherContextTypeNames)
    {
        SelectedContextTypeName = selectedContextTypeName;
        OtherContextTypeNames = otherContextTypeNames;
        HasMultipleContexts = otherContextTypeNames.Count > 0;
    }

    internal string SelectedContextTypeName { get; }

    internal IReadOnlySet<string> OtherContextTypeNames { get; }

    internal bool HasMultipleContexts { get; }

    internal static DbContextScanScope Create(string efProjectPath, string startupProjectPath, Type selectedContextType) =>
        Create(efProjectPath, startupProjectPath, selectedContextType.Name);

    internal static DbContextScanScope Create(
        string efProjectPath,
        string startupProjectPath,
        string selectedContextTypeName)
    {
        var projectPaths = LinqProjectSourceWalker.CollectScanProjectPaths(efProjectPath, startupProjectPath);
        var projectDirectories = projectPaths
            .Select(static path => Path.GetDirectoryName(path)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var discovered = DbContextClassDiscovery.DiscoverDbContextTypeNames(projectDirectories);
        var selectedName = NormalizeContextTypeName(selectedContextTypeName);

        var others = discovered
            .Where(name => !string.Equals(name, selectedName, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        if (!discovered.Contains(selectedName, StringComparer.Ordinal)
            && !others.Contains(selectedName, StringComparer.Ordinal))
        {
            // Context may live only in the built assembly; still filter explicit other types.
        }

        return new DbContextScanScope(selectedName, others);
    }

    private static string NormalizeContextTypeName(string contextTypeName)
    {
        var trimmed = contextTypeName.Trim();
        var lastDot = trimmed.LastIndexOf('.');

        return lastDot >= 0 ? trimmed[(lastDot + 1)..] : trimmed;
    }
}
