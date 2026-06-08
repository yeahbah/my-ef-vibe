namespace MyEfVibe;

internal static class EntityFrameworkProviderDiscovery
{
    internal static ProviderDescriptor? TryDiscoverFromProject(string csprojAbsolutePath)
    {
        if (string.IsNullOrWhiteSpace(csprojAbsolutePath))
        {
            return null;
        }

        var packageIds = CollectProviderPackageIds(Path.GetFullPath(csprojAbsolutePath));

        return packageIds.Count == 1
            ? EntityFrameworkProviderCatalog.CreateDescriptor(packageIds[0])
            : null;
    }

    internal static IReadOnlyList<string> CollectProviderPackageIds(string csprojAbsolutePath)
    {
        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectProviderPackageIdsRecursive(csprojAbsolutePath, new HashSet<string>(StringComparer.OrdinalIgnoreCase), packageIds);

        return packageIds.ToArray();
    }

    internal static string FormatMultipleProvidersMessage(IReadOnlyList<string> packageIds)
    {
        var listed = string.Join(", ", packageIds.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase));

        return "Multiple EF provider packages were found on `-p`: "
               + listed
               + ". Reference only one provider in the EF project or pass `--provider` with a provider alias or package id.";
    }

    internal static bool TryDescribeAmbiguousProviders(
        string csprojAbsolutePath,
        out string? message)
    {
        message = null;

        if (string.IsNullOrWhiteSpace(csprojAbsolutePath))
        {
            return false;
        }

        var packageIds = CollectProviderPackageIds(Path.GetFullPath(csprojAbsolutePath));

        if (packageIds.Count <= 1)
        {
            return false;
        }

        message = FormatMultipleProvidersMessage(packageIds);

        return true;
    }

    private static void CollectProviderPackageIdsRecursive(
        string csprojAbsolutePath,
        HashSet<string> visitedProjects,
        HashSet<string> packageIds)
    {
        if (!visitedProjects.Add(csprojAbsolutePath) || !File.Exists(csprojAbsolutePath))
        {
            return;
        }

        foreach (var packageId in CsprojInspector.EnumeratePackageReferenceIds(csprojAbsolutePath))
        {
            if (EntityFrameworkProviderCatalog.IsEntityFrameworkProviderPackage(packageId))
            {
                packageIds.Add(packageId);
            }
        }

        foreach (var reference in CsprojInspector.GetProjectReferencePaths(csprojAbsolutePath))
        {
            CollectProviderPackageIdsRecursive(reference, visitedProjects, packageIds);
        }
    }
}
