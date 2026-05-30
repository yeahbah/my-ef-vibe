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

    private readonly Dictionary<string, List<NativeAsset>> _nativeAssetsByFileName =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<PackageLibrary> _packageLibraries = [];
    private readonly string _nuGetPackagesRoot;

    private WorkspaceDepsManifest(string nuGetPackagesRoot)
    {
        _nuGetPackagesRoot = nuGetPackagesRoot;
    }

    private sealed record AssemblyAsset(Version? Version, string Path, int Rank);

    private sealed record NativeAsset(string Path, int Rank);

    private sealed record PackageLibrary(string PackageId, Version? Version, string PackageFolder);

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
            var manifest = new WorkspaceDepsManifest(nuGetPackagesRoot);
            manifest.IndexPackageLibraries(librariesProperty, nuGetPackagesRoot);
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

            var normalizedRelativePath = runtimeAsset.Name.Replace('/', Path.DirectorySeparatorChar);
            var fileName = Path.GetFileName(normalizedRelativePath);

            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;

            TryAddAsset(
                normalizedRelativePath,
                fileName,
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

            var normalizedRelativePath = runtimeAsset.Name.Replace('/', Path.DirectorySeparatorChar);
            var fileName = Path.GetFileName(normalizedRelativePath);

            if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                TryAddAsset(
                    normalizedRelativePath,
                    fileName,
                    packageFolder,
                    outputDirectory,
                    libraryVersion,
                    rank,
                    isProjectLibrary);
            }
            else if (IsNativeLibraryFileName(fileName))
            {
                TryAddNativeAsset(normalizedRelativePath, fileName, packageFolder, outputDirectory, rank);
            }
        }
    }

    internal bool TryResolveNativeLibrary(out string absolutePath, params string[] candidateFileNames)
    {
        absolutePath = string.Empty;

        foreach (var candidateFileName in candidateFileNames)
        {
            if (string.IsNullOrWhiteSpace(candidateFileName))
                continue;

            if (!_nativeAssetsByFileName.TryGetValue(candidateFileName, out var assets)
                || assets.Count == 0)
                continue;

            absolutePath = assets
                .OrderByDescending(static asset => asset.Rank)
                .First()
                .Path;

            return true;
        }

        return false;
    }

    private static bool IsNativeLibraryFileName(string fileName)
    {
        if (fileName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
            return true;

        return fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
               && fileName.Contains("e_sqlite3", StringComparison.OrdinalIgnoreCase);
    }

    private void TryAddNativeAsset(
        string normalizedRelativePath,
        string fileName,
        string? packageFolder,
        string outputDirectory,
        int rank)
    {
        var absolutePath = packageFolder is null
            ? Path.Combine(outputDirectory, fileName)
            : Path.Combine(packageFolder, normalizedRelativePath);

        if (!File.Exists(absolutePath))
            return;

        if (!_nativeAssetsByFileName.TryGetValue(fileName, out var assets))
        {
            assets = [];
            _nativeAssetsByFileName[fileName] = assets;
        }

        var duplicatePath = assets.FindIndex(asset =>
            string.Equals(asset.Path, absolutePath, StringComparison.OrdinalIgnoreCase));

        if (duplicatePath >= 0)
        {
            if (assets[duplicatePath].Rank <= rank)
                return;

            assets.RemoveAt(duplicatePath);
        }

        assets.Add(new NativeAsset(absolutePath, rank));
    }

    private void TryAddAsset(
        string normalizedRelativePath,
        string fileName,
        string? packageFolder,
        string outputDirectory,
        Version? libraryVersion,
        int rank,
        bool isProjectLibrary)
    {
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

        foreach (var library in other._packageLibraries)
        {
            if (_packageLibraries.Any(existing =>
                    string.Equals(existing.PackageId, library.PackageId, StringComparison.OrdinalIgnoreCase)
                    && VersionsEqual(existing.Version, library.Version)))
                continue;

            _packageLibraries.Add(library);
        }

        foreach (var (fileName, otherAssets) in other._nativeAssetsByFileName)
        {
            if (!_nativeAssetsByFileName.TryGetValue(fileName, out var assets))
            {
                _nativeAssetsByFileName[fileName] = [..otherAssets];
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
    }

    internal bool TryResolve(string? assemblySimpleName, out string absolutePath)
        => TryResolve(assemblySimpleName, allowProviderNuGetFallback: true, out absolutePath);

    internal bool TryResolve(
        string? assemblySimpleName,
        bool allowProviderNuGetFallback,
        out string absolutePath)
    {
        if (string.IsNullOrEmpty(assemblySimpleName))
        {
            absolutePath = string.Empty;
            return false;
        }

        return TryResolve(new AssemblyName(assemblySimpleName), allowProviderNuGetFallback, out absolutePath);
    }

    internal bool TryResolve(AssemblyName requested, out string absolutePath)
        => TryResolve(requested, allowProviderNuGetFallback: true, out absolutePath);

    internal bool TryResolve(
        AssemblyName requested,
        bool allowProviderNuGetFallback,
        out string absolutePath)
    {
        absolutePath = string.Empty;

        if (string.IsNullOrEmpty(requested.Name))
            return false;

        if (_assetsBySimpleName.TryGetValue(requested.Name, out var assets) && assets.Count > 0)
        {
            var chosen = ChooseBestAsset(requested.Version, assets);

            if (chosen is not null)
            {
                absolutePath = chosen.Path;
                return true;
            }
        }

        if (TryResolveFromPackageLib(requested, out absolutePath))
            return true;

        if (!allowProviderNuGetFallback)
            return false;

        if (requested.Version is null || AssemblyResolutionHelpers.IsZeroVersion(requested.Version))
            return TryResolveLatestProviderFromNuGetPackageFolder(requested, out absolutePath);

        return TryResolveFromNuGetPackageFolder(requested, out absolutePath);
    }

    private void IndexPackageLibraries(JsonElement librariesProperty, string nuGetPackagesRoot)
    {
        foreach (var library in librariesProperty.EnumerateObject())
        {
            if (!library.Value.TryGetProperty("type", out var typeProperty)
                || !string.Equals(typeProperty.GetString(), "package", StringComparison.Ordinal))
                continue;

            if (!library.Value.TryGetProperty("path", out var packagePathProperty))
                continue;

            var slashIndex = library.Name.LastIndexOf('/');

            if (slashIndex <= 0 || slashIndex >= library.Name.Length - 1)
                continue;

            var packageId = library.Name[..slashIndex];
            var packageFolder = Path.Combine(nuGetPackagesRoot, packagePathProperty.GetString()!);

            _packageLibraries.Add(new PackageLibrary(
                packageId,
                TryParseVersionFromLibraryName(library.Name),
                packageFolder));
        }
    }

    private bool TryResolveFromPackageLib(AssemblyName requested, out string absolutePath)
    {
        absolutePath = string.Empty;

        if (string.IsNullOrEmpty(requested.Name))
            return false;

        foreach (var library in _packageLibraries)
        {
            if (!string.Equals(library.PackageId, requested.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (requested.Version is not null
                && !AssemblyResolutionHelpers.IsZeroVersion(requested.Version)
                && library.Version is not null
                && !AssemblyResolutionHelpers.VersionsMatch(requested.Version, library.Version))
                continue;

            var dllPath = FindAssemblyDllInPackage(library.PackageFolder, requested.Name);

            if (dllPath is null)
                continue;

            absolutePath = dllPath;

            return true;
        }

        return false;
    }

    private bool TryResolveFromNuGetPackageFolder(AssemblyName requested, out string absolutePath)
    {
        absolutePath = string.Empty;

        if (string.IsNullOrEmpty(requested.Name)
            || requested.Version is null
            || AssemblyResolutionHelpers.IsZeroVersion(requested.Version))
            return false;

        foreach (var versionFolder in EnumerateNuGetVersionFolderCandidates(requested.Version))
        {
            var packageFolder = Path.Combine(
                _nuGetPackagesRoot,
                ProviderAssemblyNames.GetNuGetPackageFolderName(requested.Name!),
                versionFolder);

            var dllPath = FindAssemblyDllInPackage(packageFolder, requested.Name);

            if (dllPath is null)
                continue;

            absolutePath = dllPath;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves EF provider packages from the global NuGet cache when the workspace project does not
    /// reference them (e.g. <c>--provider npgsql</c> against a SqlServer-only EF library).
    /// </summary>
    private bool TryResolveLatestProviderFromNuGetPackageFolder(AssemblyName requested, out string absolutePath)
    {
        absolutePath = string.Empty;

        if (string.IsNullOrEmpty(requested.Name)
            || !ProviderAssemblyNames.IsKnownProviderAssembly(requested.Name))
            return false;

        return TryResolveLatestFromNuGetPackageFolder(requested, out absolutePath);
    }

    private bool TryResolveLatestFromNuGetPackageFolder(AssemblyName requested, out string absolutePath)
    {
        absolutePath = string.Empty;

        if (string.IsNullOrEmpty(requested.Name))
            return false;

        var packageRoot = Path.Combine(
            _nuGetPackagesRoot,
            ProviderAssemblyNames.GetNuGetPackageFolderName(requested.Name));

        if (!Directory.Exists(packageRoot))
            return false;

        var preferredMajor = TryGetReferencedEntityFrameworkCoreMajor();

        foreach (var versionFolder in EnumerateNuGetVersionFolders(packageRoot, preferredMajor))
        {
            var dllPath = FindAssemblyDllInPackage(
                Path.Combine(packageRoot, versionFolder),
                requested.Name);

            if (dllPath is null)
                continue;

            absolutePath = dllPath;

            return true;
        }

        return false;
    }

    private int? TryGetReferencedEntityFrameworkCoreMajor()
    {
        if (!_assetsBySimpleName.TryGetValue("Microsoft.EntityFrameworkCore", out var assets)
            || assets.Count == 0)
            return null;

        var chosen = ChooseBestAsset(requestedVersion: null, assets);

        return chosen?.Version?.Major;
    }

    private static IEnumerable<string> EnumerateNuGetVersionFolders(string packageRoot, int? preferredMajor)
    {
        var versionFolders = Directory.EnumerateDirectories(packageRoot)
            .Select(static path => Path.GetFileName(path)!)
            .Where(static folder => Version.TryParse(folder, out _))
            .OrderByDescending(static folder => Version.Parse(folder))
            .ToArray();

        if (preferredMajor is null)
            return versionFolders;

        var matchingMajor = versionFolders
            .Where(folder => Version.Parse(folder).Major == preferredMajor.Value)
            .ToArray();

        return matchingMajor.Length > 0 ? matchingMajor : versionFolders;
    }

    private static IEnumerable<string> EnumerateNuGetVersionFolderCandidates(Version requestedVersion)
    {
        var build = requestedVersion.Build == -1 ? 0 : requestedVersion.Build;
        var revision = requestedVersion.Revision == -1 ? 0 : requestedVersion.Revision;

        yield return $"{requestedVersion.Major}.{requestedVersion.Minor}.{build}";

        if (revision > 0)
            yield return $"{requestedVersion.Major}.{requestedVersion.Minor}.{build}.{revision}";
    }

    private static string? FindAssemblyDllInPackage(string packageFolder, string assemblySimpleName)
    {
        var libRoot = Path.Combine(packageFolder, "lib");

        if (!Directory.Exists(libRoot))
            return null;

        foreach (var tfmDirectory in EnumeratePreferredLibDirectories(libRoot))
        {
            var candidate = Path.Combine(tfmDirectory, $"{assemblySimpleName}.dll");

            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> EnumeratePreferredLibDirectories(string libRoot)
    {
        return Directory.EnumerateDirectories(libRoot)
            .OrderBy(GetLibDirectoryRank)
            .ThenByDescending(static path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static int GetLibDirectoryRank(string tfmDirectory)
    {
        var tfm = Path.GetFileName(tfmDirectory);

        if (IsMobileOrLegacyLibDirectory(tfm))
            return 1_000;

        if (TryGetNetCoreLibRank(tfm, out var netCoreRank))
            return netCoreRank;

        if (TryGetNetStandardLibRank(tfm, out var netStandardRank))
            return netStandardRank;

        return 500;
    }

    private static bool IsMobileOrLegacyLibDirectory(string tfm)
        => tfm.Contains("android", StringComparison.OrdinalIgnoreCase)
           || tfm.Contains("ios", StringComparison.OrdinalIgnoreCase)
           || tfm.Contains("tvos", StringComparison.OrdinalIgnoreCase)
           || tfm.StartsWith("xamarin", StringComparison.OrdinalIgnoreCase)
           || tfm.StartsWith("monoandroid", StringComparison.OrdinalIgnoreCase)
           || string.Equals(tfm, "net461", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetNetCoreLibRank(string tfm, out int rank)
    {
        rank = 0;

        if (!tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase)
            || tfm.Contains('-', StringComparison.Ordinal))
            return false;

        if (!Version.TryParse(tfm["net".Length..], out var version))
            return false;

        rank = 100 - (version.Major * 10 + version.Minor);

        return true;
    }

    private static bool TryGetNetStandardLibRank(string tfm, out int rank)
    {
        rank = 0;

        if (!tfm.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!Version.TryParse(tfm["netstandard".Length..], out var version))
            return false;

        rank = 200 - (version.Major * 10 + version.Minor);

        return true;
    }

    private static AssemblyAsset? ChooseBestAsset(Version? requestedVersion, List<AssemblyAsset> assets)
    {
        if (assets.Count == 1)
        {
            var only = assets[0];

            if (requestedVersion is null || requestedVersion == new Version(0, 0, 0, 0))
                return only;

            if (only.Version is null || AssemblyResolutionHelpers.VersionsMatch(requestedVersion, only.Version))
                return only;

            return null;
        }

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

        var exact = withVersions.FirstOrDefault(asset =>
            AssemblyResolutionHelpers.VersionsMatch(requestedVersion, asset.Version));

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
