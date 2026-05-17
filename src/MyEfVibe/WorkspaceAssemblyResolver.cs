using System.Reflection;
using System.Runtime.Loader;

namespace MyEfVibe;

internal sealed class WorkspaceAssemblyResolver : IDisposable
{
    private readonly AssemblyDependencyResolver _dependencyResolver;
    private readonly WorkspaceDepsManifest? _depsManifest;
    private readonly SharedFrameworkCatalog _sharedFrameworkCatalog;
    private readonly string _outputDirectory;
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

    private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        if (_depsManifest?.TryResolve(assemblyName.Name, out var depsPath) == true)
            return context.LoadFromAssemblyPath(depsPath);

        var resolvedPath = _dependencyResolver.ResolveAssemblyToPath(assemblyName);

        if (resolvedPath is not null)
            return context.LoadFromAssemblyPath(resolvedPath);

        var outputCandidate = Path.Combine(_outputDirectory, $"{assemblyName.Name}.dll");

        if (File.Exists(outputCandidate))
            return context.LoadFromAssemblyPath(outputCandidate);

        if (_sharedFrameworkCatalog.TryResolve(assemblyName.Name!, out var sharedPath))
            return context.LoadFromAssemblyPath(sharedPath);

        return null;
    }

    public void Dispose()
    {
        AssemblyLoadContext.Default.Resolving -= _resolveHandler;
    }
}
