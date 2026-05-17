using System.Collections.Immutable;
using System.Text.Json;

namespace MyEfVibe;

/// <summary>
/// Resolves runtime assembly paths from a built project's <c>.deps.json</c>, including NuGet packages
/// that <see cref="AssemblyDependencyResolver"/> does not return for library outputs.
/// </summary>
internal sealed class WorkspaceDepsManifest
{
    private readonly Dictionary<string, string> _simpleNameToPath =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, int> _simpleNameRank =
        new(StringComparer.OrdinalIgnoreCase);

    private WorkspaceDepsManifest()
    {
    }

    internal static WorkspaceDepsManifest? TryLoad(string entryAssemblyPath)
    {
        var outputDirectory = Path.GetDirectoryName(entryAssemblyPath);

        if (string.IsNullOrEmpty(outputDirectory))
            return null;

        var depsPath = Path.Combine(
            outputDirectory,
            $"{Path.GetFileNameWithoutExtension(entryAssemblyPath)}.deps.json");

        if (!File.Exists(depsPath))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(depsPath));

            if (!document.RootElement.TryGetProperty("runtimeTarget", out var runtimeTargetProperty)
                || !document.RootElement.TryGetProperty("targets", out var targetsProperty)
                || !document.RootElement.TryGetProperty("libraries", out var librariesProperty))
                return null;

            if (!runtimeTargetProperty.TryGetProperty("name", out var runtimeTargetNameProperty))
                return null;

            var runtimeTargetName = runtimeTargetNameProperty.GetString();

            if (string.IsNullOrWhiteSpace(runtimeTargetName)
                || !targetsProperty.TryGetProperty(runtimeTargetName, out var targetNode))
                return null;

            var nuGetPackagesRoot = ResolveNuGetPackagesRoot();
            var manifest = new WorkspaceDepsManifest();
            var runtimeFallbacks = HostRuntimeIdentifier.GetRuntimeFallbacks();

            foreach (var library in targetNode.EnumerateObject())
            {
                if (library.Value.TryGetProperty("runtime", out var runtimeAssets))
                    manifest.AddRuntimeAssets(
                        runtimeAssets,
                        librariesProperty,
                        library.Name,
                        nuGetPackagesRoot,
                        outputDirectory,
                        runtimeIdentifier: null,
                        runtimeFallbacks);

                if (library.Value.TryGetProperty("runtimeTargets", out var runtimeTargetAssets))
                    manifest.AddRuntimeTargetAssets(
                        runtimeTargetAssets,
                        librariesProperty,
                        library.Name,
                        nuGetPackagesRoot,
                        outputDirectory,
                        runtimeFallbacks);
            }

            return manifest;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void AddRuntimeAssets(
        JsonElement runtimeAssets,
        JsonElement librariesProperty,
        string libraryName,
        string nuGetPackagesRoot,
        string outputDirectory,
        string? runtimeIdentifier,
        IReadOnlyList<string> runtimeFallbacks)
    {
        var packageFolder = ResolvePackageFolder(librariesProperty, libraryName, nuGetPackagesRoot);

        foreach (var runtimeAsset in runtimeAssets.EnumerateObject())
        {
            var rank = runtimeIdentifier is null
                ? runtimeFallbacks.Count
                : HostRuntimeIdentifier.GetFallbackRank(runtimeIdentifier, runtimeFallbacks);

            TryAddAsset(
                runtimeAsset.Name,
                packageFolder,
                outputDirectory,
                rank);
        }
    }

    private void AddRuntimeTargetAssets(
        JsonElement runtimeTargetAssets,
        JsonElement librariesProperty,
        string libraryName,
        string nuGetPackagesRoot,
        string outputDirectory,
        IReadOnlyList<string> runtimeFallbacks)
    {
        var packageFolder = ResolvePackageFolder(librariesProperty, libraryName, nuGetPackagesRoot);

        foreach (var runtimeAsset in runtimeTargetAssets.EnumerateObject())
        {
            if (!runtimeAsset.Value.TryGetProperty("rid", out var ridProperty))
                continue;

            var rid = ridProperty.GetString();

            if (string.IsNullOrWhiteSpace(rid))
                continue;

            var rank = HostRuntimeIdentifier.GetFallbackRank(rid, runtimeFallbacks);

            if (rank == int.MaxValue)
                continue;

            TryAddAsset(runtimeAsset.Name, packageFolder, outputDirectory, rank);
        }
    }

    private void TryAddAsset(
        string relativeAssetPath,
        string? packageFolder,
        string outputDirectory,
        int rank)
    {
        var normalizedRelativePath = relativeAssetPath.Replace('/', Path.DirectorySeparatorChar);
        var fileName = Path.GetFileName(normalizedRelativePath);

        if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return;

        var absolutePath = packageFolder is null
            ? Path.Combine(outputDirectory, fileName)
            : Path.Combine(packageFolder, normalizedRelativePath);

        if (!File.Exists(absolutePath))
            return;

        var simpleName = Path.GetFileNameWithoutExtension(fileName);

        if (_simpleNameRank.TryGetValue(simpleName, out var existingRank) && existingRank <= rank)
            return;

        _simpleNameToPath[simpleName] = absolutePath;
        _simpleNameRank[simpleName] = rank;
    }

    private static string? ResolvePackageFolder(
        JsonElement librariesProperty,
        string libraryName,
        string nuGetPackagesRoot)
    {
        if (!librariesProperty.TryGetProperty(libraryName, out var libraryMetadata)
            || !libraryMetadata.TryGetProperty("path", out var packagePathProperty))
            return null;

        return Path.Combine(nuGetPackagesRoot, packagePathProperty.GetString()!);
    }

    internal bool TryResolve(string? assemblySimpleName, out string absolutePath)
    {
        if (string.IsNullOrEmpty(assemblySimpleName))
        {
            absolutePath = string.Empty;
            return false;
        }

        return _simpleNameToPath.TryGetValue(assemblySimpleName, out absolutePath!);
    }

    internal ImmutableArray<string> RuntimeAssemblyPaths
        => _simpleNameToPath.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray();

    private static string ResolveNuGetPackagesRoot()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("NUGET_PACKAGES");

        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages");
    }
}
