using System.Reflection;
using System.Runtime.Loader;

namespace MyEfVibe;

internal sealed class WorkspaceAssemblyResolver : IDisposable
{
    private readonly AssemblyDependencyResolver _dependencyResolver;
    private readonly WorkspaceDepsManifest? _depsManifest;
    private readonly SharedFrameworkCatalog _sharedFrameworkCatalog;
    private readonly string _outputDirectory;
    private readonly Dictionary<string, Assembly> _resolvedAssemblies =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<AssemblyLoadContext, AssemblyName, Assembly?> _resolveHandler;

    private WorkspaceAssemblyResolver(
        string entryAssemblyPath,
        SharedFrameworkCatalog sharedFrameworkCatalog,
        WorkspaceDepsManifest? depsManifest)
    {
        _dependencyResolver = new AssemblyDependencyResolver(entryAssemblyPath);
        _depsManifest = depsManifest;
        _sharedFrameworkCatalog = sharedFrameworkCatalog;
        _outputDirectory = Path.GetDirectoryName(entryAssemblyPath)!;

        _resolveHandler = OnResolving;

        AssemblyLoadContext.Default.Resolving += _resolveHandler;
    }

    internal WorkspaceDepsManifest? DepsManifest => _depsManifest;

    internal static WorkspaceAssemblyResolver Install(
        string entryAssemblyPath,
        SharedFrameworkCatalog sharedFrameworkCatalog,
        WorkspaceDepsManifest? depsManifest)
        => new(entryAssemblyPath, sharedFrameworkCatalog, depsManifest);

    internal Assembly LoadEntryAssembly(string entryAssemblyPath)
        => AssemblyResolutionHelpers.LoadFromPath(AssemblyLoadContext.Default, entryAssemblyPath);

    internal void PreloadCorePackages()
    {
        if (_depsManifest is null)
            return;

        WorkspaceDependencyLoader.PreloadCorePackages(AssemblyLoadContext.Default, _depsManifest);
    }

    internal void PreloadStartupReferenceClosure(string startupAssemblyPath, string entryAssemblyPath)
    {
        if (_depsManifest is null)
            return;

        WorkspaceDependencyLoader.PreloadStartupReferenceClosure(
            AssemblyLoadContext.Default,
            _depsManifest,
            startupAssemblyPath,
            entryAssemblyPath);
    }

    internal void PreloadDependencies(
        string entryAssemblyPath,
        string? additionalReferenceClosureAssemblyPath = null)
        => WorkspaceDependencyLoader.Preload(
            AssemblyLoadContext.Default,
            _dependencyResolver,
            entryAssemblyPath,
            _depsManifest,
            additionalReferenceClosureAssemblyPath);

    internal bool IsOutputDirectoryAssembly(Assembly assembly)
    {
        if (string.IsNullOrEmpty(assembly.Location))
            return false;

        return assembly.Location.StartsWith(_outputDirectory, StringComparison.OrdinalIgnoreCase);
    }

    internal Assembly? ResolveAssembly(string assemblySimpleName)
    {
        if (string.IsNullOrWhiteSpace(assemblySimpleName))
            return null;

        return OnResolving(AssemblyLoadContext.Default, new AssemblyName(assemblySimpleName));
    }

    private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        var simpleName = assemblyName.Name;

        if (string.IsNullOrEmpty(simpleName))
            return null;

        var cacheKey = AssemblyResolutionHelpers.GetCacheKey(assemblyName);

        if (_resolvedAssemblies.TryGetValue(cacheKey, out var cached)
            && AssemblyResolutionHelpers.VersionMatches(assemblyName, cached))
            return cached;

        if (IsSystemTextJson(simpleName))
        {
            var jsonAssembly = ResolveSystemTextJson(context, assemblyName);

            if (jsonAssembly is not null)
            {
                _resolvedAssemblies[cacheKey] = jsonAssembly;
                return jsonAssembly;
            }
        }

        Assembly? loaded = null;

        var hasExplicitVersion = assemblyName.Version is not null
                                 && !AssemblyResolutionHelpers.IsZeroVersion(assemblyName.Version);

        // Prefer version-aware .deps.json resolution when a specific version is requested (e.g. DiagnosticSource 9 for EF 9).
        if (hasExplicitVersion && _depsManifest?.TryResolve(assemblyName, out var versionedDepsPath) == true)
            loaded = TryLoad(context, versionedDepsPath);

        if (loaded is null)
        {
            var resolvedPath = _dependencyResolver.ResolveAssemblyToPath(assemblyName);

            if (resolvedPath is not null
                && AssemblyResolutionHelpers.IsCompatibleWithRequestedVersion(assemblyName, resolvedPath))
                loaded = TryLoad(context, resolvedPath);
        }

        if (loaded is null && _depsManifest?.TryResolve(assemblyName, out var depsPath) == true)
            loaded = TryLoad(context, depsPath);

        if (loaded is null)
        {
            var outputCandidate = Path.Combine(_outputDirectory, $"{simpleName}.dll");

            if (File.Exists(outputCandidate)
                && AssemblyResolutionHelpers.IsCompatibleWithRequestedVersion(assemblyName, outputCandidate)
                && (!IsSystemTextJson(simpleName) || SystemTextJsonPathSupportsWeb(outputCandidate)))
                loaded = TryLoad(context, outputCandidate);
        }

        if (loaded is null
            && _sharedFrameworkCatalog.TryResolve(simpleName, out var sharedPath)
            && AssemblyResolutionHelpers.IsCompatibleWithRequestedVersion(assemblyName, sharedPath))
            loaded = TryLoad(context, sharedPath);

        if (loaded is not null)
            _resolvedAssemblies[cacheKey] = loaded;

        return loaded;
    }

    private Assembly? ResolveSystemTextJson(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        if (SystemTextJsonCapabilities.TryGetLoaded(out var existing)
            && SystemTextJsonCapabilities.IsCompatible(existing)
            && AssemblyResolutionHelpers.VersionMatches(assemblyName, existing))
            return existing;

        if (_sharedFrameworkCatalog.TryResolve(SystemTextJsonCapabilities.AssemblySimpleName, out var sharedPath)
            && AssemblyResolutionHelpers.IsCompatibleWithRequestedVersion(assemblyName, sharedPath))
        {
            var shared = TryLoad(context, sharedPath);

            if (shared is not null && SystemTextJsonCapabilities.IsCompatible(shared))
                return shared;
        }

        if (_depsManifest?.TryResolve(assemblyName, out var depsPath) == true
            && SystemTextJsonPathSupportsWeb(depsPath))
        {
            var fromDeps = TryLoad(context, depsPath);

            if (fromDeps is not null && SystemTextJsonCapabilities.IsCompatible(fromDeps))
                return fromDeps;
        }

        var outputCandidate = Path.Combine(_outputDirectory, $"{SystemTextJsonCapabilities.AssemblySimpleName}.dll");

        if (File.Exists(outputCandidate)
            && AssemblyResolutionHelpers.IsCompatibleWithRequestedVersion(assemblyName, outputCandidate)
            && SystemTextJsonPathSupportsWeb(outputCandidate))
        {
            var fromOutput = TryLoad(context, outputCandidate);

            if (fromOutput is not null && SystemTextJsonCapabilities.IsCompatible(fromOutput))
                return fromOutput;
        }

        return null;
    }

    private static bool IsSystemTextJson(string? simpleName)
        => string.Equals(simpleName, SystemTextJsonCapabilities.AssemblySimpleName, StringComparison.OrdinalIgnoreCase);

    private static bool SystemTextJsonPathSupportsWeb(string absolutePath)
    {
        try
        {
            var version = AssemblyName.GetAssemblyName(absolutePath).Version;

            return version is not null && version.Major >= 5;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch (FileLoadException)
        {
            return false;
        }
    }

    private static Assembly? TryLoad(AssemblyLoadContext context, string absolutePath)
    {
        try
        {
            return AssemblyResolutionHelpers.LoadFromPath(context, absolutePath);
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (FileLoadException)
        {
            return AssemblyResolutionHelpers.GetLoadedAssembly(
                AssemblyName.GetAssemblyName(absolutePath),
                absolutePath);
        }
    }

    public void Dispose()
    {
        AssemblyLoadContext.Default.Resolving -= _resolveHandler;
    }
}
