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
        => AssemblyLoadContext.Default.LoadFromAssemblyPath(entryAssemblyPath);

    internal void PreloadDependencies(string entryAssemblyPath)
        => WorkspaceDependencyLoader.Preload(
            AssemblyLoadContext.Default,
            _dependencyResolver,
            entryAssemblyPath,
            _depsManifest);

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

        Assembly? loaded = null;

        var resolvedPath = _dependencyResolver.ResolveAssemblyToPath(assemblyName);

        if (resolvedPath is not null)
            loaded = TryLoad(context, resolvedPath);

        if (loaded is null && _depsManifest?.TryResolve(assemblyName, out var depsPath) == true)
            loaded = TryLoad(context, depsPath);

        if (loaded is null)
        {
            var outputCandidate = Path.Combine(_outputDirectory, $"{simpleName}.dll");

            if (File.Exists(outputCandidate))
                loaded = TryLoad(context, outputCandidate);
        }

        if (loaded is null
            && _sharedFrameworkCatalog.TryResolve(simpleName, out var sharedPath))
            loaded = TryLoad(context, sharedPath);

        if (loaded is not null)
            _resolvedAssemblies[cacheKey] = loaded;

        return loaded;
    }

    private static Assembly? TryLoad(AssemblyLoadContext context, string absolutePath)
    {
        try
        {
            return context.LoadFromAssemblyPath(absolutePath);
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

    public void Dispose()
    {
        AssemblyLoadContext.Default.Resolving -= _resolveHandler;
    }
}
