using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

namespace MyEfVibe;

/// <summary>
/// Resolves runtime assembly paths from a built project's <c>.deps.json</c>, including NuGet packages
/// that <see cref="AssemblyDependencyResolver"/> does not return for library outputs.
/// </summary>
internal sealed class WorkspaceDepsManifest
{
    private readonly Dictionary<string, List<AssemblyAsset>> _assetsBySimpleName =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _projectAssemblyPaths =
        new(StringComparer.OrdinalIgnoreCase);

    private WorkspaceDepsManifest()
    {
    }

    private sealed record AssemblyAsset(Version? Version, string Path, int Rank);

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
        var libraryVersion = TryParseVersionFromLibraryName(libraryName);
        var isProjectLibrary = IsProjectLibrary(librariesProperty, libraryName);

        foreach (var runtimeAsset in runtimeAssets.EnumerateObject())
        {
            var rank = runtimeIdentifier is null
                ? runtimeFallbacks.Count
                : HostRuntimeIdentifier.GetFallbackRank(runtimeIdentifier, runtimeFallbacks);

            TryAddAsset(
                runtimeAsset.Name,
                packageFolder,
                outputDirectory,
                libraryVersion,
                rank,
                isProjectLibrary);
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
        var libraryVersion = TryParseVersionFromLibraryName(libraryName);
        var isProjectLibrary = IsProjectLibrary(librariesProperty, libraryName);

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

            TryAddAsset(runtimeAsset.Name, packageFolder, outputDirectory, libraryVersion, rank, isProjectLibrary);
        }
    }

    private void TryAddAsset(
        string relativeAssetPath,
        string? packageFolder,
        string outputDirectory,
        Version? libraryVersion,
        int rank,
        bool isProjectLibrary)
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
        var assemblyVersion = TryReadAssemblyVersion(absolutePath) ?? libraryVersion;

        if (!_assetsBySimpleName.TryGetValue(simpleName, out var assets))
        {
            assets = [];
            _assetsBySimpleName[simpleName] = assets;
        }

        var duplicatePath = assets.FindIndex(asset =>
            string.Equals(asset.Path, absolutePath, StringComparison.OrdinalIgnoreCase));

        if (duplicatePath >= 0)
        {
            var existing = assets[duplicatePath];

            if (existing.Rank <= rank)
                return;

            assets.RemoveAt(duplicatePath);
        }

        var sameVersionIndex = assets.FindIndex(asset => VersionsEqual(asset.Version, assemblyVersion));

        if (sameVersionIndex >= 0)
        {
            var existing = assets[sameVersionIndex];

            if (existing.Rank <= rank)
                return;

            assets.RemoveAt(sameVersionIndex);
        }

        assets.Add(new AssemblyAsset(assemblyVersion, absolutePath, rank));

        if (isProjectLibrary)
            _projectAssemblyPaths.Add(absolutePath);
    }

    internal IEnumerable<string> EnumerateProjectAssemblyPaths() => _projectAssemblyPaths;

    internal static WorkspaceDepsManifest? Merge(WorkspaceDepsManifest? primary, WorkspaceDepsManifest? secondary)
    {
        if (primary is null)
            return secondary;

        if (secondary is null)
            return primary;

        primary.ImportFrom(secondary);

        return primary;
    }

    private void ImportFrom(WorkspaceDepsManifest other)
    {
        foreach (var (simpleName, otherAssets) in other._assetsBySimpleName)
        {
            if (!_assetsBySimpleName.TryGetValue(simpleName, out var assets))
            {
                _assetsBySimpleName[simpleName] = [..otherAssets];
                continue;
            }

            foreach (var asset in otherAssets)
            {
                if (assets.Any(existing =>
                        string.Equals(existing.Path, asset.Path, StringComparison.OrdinalIgnoreCase)))
                    continue;

                assets.Add(asset);
            }
        }

        _projectAssemblyPaths.UnionWith(other._projectAssemblyPaths);
    }

    internal bool TryResolve(string? assemblySimpleName, out string absolutePath)
    {
        if (string.IsNullOrEmpty(assemblySimpleName))
        {
            absolutePath = string.Empty;
            return false;
        }

        return TryResolve(new AssemblyName(assemblySimpleName), out absolutePath);
    }

    internal bool TryResolve(AssemblyName requested, out string absolutePath)
    {
        absolutePath = string.Empty;

        if (string.IsNullOrEmpty(requested.Name)
            || !_assetsBySimpleName.TryGetValue(requested.Name, out var assets)
            || assets.Count == 0)
            return false;

        var chosen = ChooseBestAsset(requested.Version, assets);

        if (chosen is null)
            return false;

        absolutePath = chosen.Path;

        return true;
    }

    private static AssemblyAsset? ChooseBestAsset(Version? requestedVersion, List<AssemblyAsset> assets)
    {
        if (assets.Count == 1)
            return assets[0];

        if (requestedVersion is null || requestedVersion == new Version(0, 0, 0, 0))
        {
            return assets
                .OrderByDescending(static asset => asset.Rank)
                .ThenByDescending(static asset => asset.Version ?? new Version(0, 0))
                .First();
        }

        var withVersions = assets.Where(static asset => asset.Version is not null).ToArray();

        if (withVersions.Length == 0)
            return assets.OrderByDescending(static asset => asset.Rank).First();

        var exact = withVersions.FirstOrDefault(asset => asset.Version == requestedVersion);

        if (exact is not null)
            return exact;

        var notHigherThanRequested = withVersions
            .Where(asset => asset.Version! <= requestedVersion)
            .OrderByDescending(asset => asset.Version)
            .ThenByDescending(asset => asset.Rank)
            .FirstOrDefault();

        if (notHigherThanRequested is not null)
            return notHigherThanRequested;

        // Never bind a higher assembly version to a lower reference (e.g. DiagnosticSource 10 for a 9.0.0.0 request).
        return null;
    }

    internal ImmutableArray<string> RuntimeAssemblyPaths
    {
        get
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var assets in _assetsBySimpleName.Values)
            {
                var preferred = ChooseBestAsset(requestedVersion: null, assets);

                if (preferred is not null)
                    paths.Add(preferred.Path);
            }

            return paths.ToImmutableArray();
        }
    }

    private static bool VersionsEqual(Version? left, Version? right)
    {
        if (left is null && right is null)
            return true;

        if (left is null || right is null)
            return false;

        return left == right;
    }

    private static Version? TryParseVersionFromLibraryName(string libraryName)
    {
        var slashIndex = libraryName.LastIndexOf('/');

        if (slashIndex < 0 || slashIndex >= libraryName.Length - 1)
            return null;

        return Version.TryParse(libraryName[(slashIndex + 1)..], out var parsed)
            ? parsed
            : null;
    }

    private static Version? TryReadAssemblyVersion(string absolutePath)
    {
        try
        {
            return AssemblyName.GetAssemblyName(absolutePath).Version;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (FileLoadException)
        {
            return null;
        }
    }

    private static bool IsProjectLibrary(JsonElement librariesProperty, string libraryName)
    {
        if (!librariesProperty.TryGetProperty(libraryName, out var libraryMetadata)
            || !libraryMetadata.TryGetProperty("type", out var typeProperty))
            return false;

        return string.Equals(typeProperty.GetString(), "project", StringComparison.Ordinal);
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
