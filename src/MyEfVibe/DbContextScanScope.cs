namespace MyEfVibe;

internal sealed class DbContextScanScope
{
    internal DbContextScanScope(
        string selectedContextTypeName,
        IReadOnlySet<string> otherContextTypeNames)
        : this(
            selectedContextTypeName,
            new HashSet<string>([selectedContextTypeName], StringComparer.Ordinal),
            otherContextTypeNames)
    {
    }

    internal DbContextScanScope(
        string selectedContextTypeName,
        IReadOnlySet<string> selectedContextTypeNames,
        IReadOnlySet<string> otherContextTypeNames)
    {
        SelectedContextTypeName = selectedContextTypeName;
        SelectedContextTypeNames = selectedContextTypeNames;
        OtherContextTypeNames = otherContextTypeNames;
        HasMultipleContexts = otherContextTypeNames.Count > 0;
    }

    internal string SelectedContextTypeName { get; }

    internal IReadOnlySet<string> SelectedContextTypeNames { get; }

    internal IReadOnlySet<string> OtherContextTypeNames { get; }

    internal bool HasMultipleContexts { get; }

    internal bool IsSelectedContextType(string typeName)
    {
        return SelectedContextTypeNames.Contains(typeName);
    }

    internal static DbContextScanScope Create(string efProjectPath, string startupProjectPath, Type selectedContextType)
    {
        return Create(efProjectPath, startupProjectPath, selectedContextType.Name);
    }

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

        var aliasesByContext = DbContextClassDiscovery.DiscoverDbContextTypeAliases(projectDirectories);
        var discovered = aliasesByContext.Keys.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        var selectedName = NormalizeContextTypeName(selectedContextTypeName);
        var selectedAliases = aliasesByContext.TryGetValue(selectedName, out var aliases)
            ? aliases
            : new HashSet<string>([selectedName], StringComparer.Ordinal);

        var others = discovered
            .Where(name => !string.Equals(name, selectedName, StringComparison.Ordinal))
            .SelectMany(name => aliasesByContext.TryGetValue(name, out var otherAliases)
                ? otherAliases
                : new HashSet<string>([name], StringComparer.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        if (!discovered.Contains(selectedName, StringComparer.Ordinal)
            && !others.Contains(selectedName, StringComparer.Ordinal))
        {
            // Context may live only in the built assembly; still filter explicit other types.
        }

        return new DbContextScanScope(selectedName, selectedAliases, others);
    }

    private static string NormalizeContextTypeName(string contextTypeName)
    {
        var trimmed = contextTypeName.Trim();
        var lastDot = trimmed.LastIndexOf('.');

        return lastDot >= 0 ? trimmed[(lastDot + 1)..] : trimmed;
    }
}