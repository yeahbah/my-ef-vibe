namespace MyEfVibe;

internal static class ScriptNamespaceImports
{
    private static readonly HashSet<string> BlocklistedNamespaces = new(StringComparer.Ordinal)
    {
        "System.Linq.Dynamic.Core"
    };

    internal static bool ShouldImportWorkspaceNamespace(string? namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            return false;
        }

        if (BlocklistedNamespaces.Contains(namespaceName))
        {
            return false;
        }

        // Framework namespaces are curated explicitly on ScriptSession; importing System.*
        // from workspace assemblies pulls in extension methods that conflict with LINQ (e.g. Take).
        if (namespaceName.StartsWith("System.", StringComparison.Ordinal)
            || string.Equals(namespaceName, "System", StringComparison.Ordinal))
        {
            return false;
        }

        if (namespaceName.StartsWith("Microsoft.", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    internal static IEnumerable<string> FilterWorkspaceNamespaces(IEnumerable<string> namespaceNames)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var namespaceName in namespaceNames)
        {
            if (!ShouldImportWorkspaceNamespace(namespaceName))
            {
                continue;
            }

            if (seen.Add(namespaceName))
            {
                yield return namespaceName;
            }
        }
    }
}