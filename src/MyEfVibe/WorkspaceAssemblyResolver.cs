using System.Reflection;
using System.Runtime.Loader;

namespace MyEfVibe;

internal sealed class WorkspaceAssemblyResolver : IDisposable
{
    private readonly AssemblyDependencyResolver _dependencyResolver;
    private readonly SharedFrameworkCatalog _sharedFrameworkCatalog;
    private readonly string _outputDirectory;
    private readonly Func<AssemblyLoadContext, AssemblyName, Assembly?> _resolveHandler;

    private WorkspaceAssemblyResolver(
        string entryAssemblyPath,
        SharedFrameworkCatalog sharedFrameworkCatalog)
    {
        _dependencyResolver = new AssemblyDependencyResolver(entryAssemblyPath);
        _sharedFrameworkCatalog = sharedFrameworkCatalog;
        _outputDirectory = Path.GetDirectoryName(entryAssemblyPath)!;

        _resolveHandler = OnResolving;

        AssemblyLoadContext.Default.Resolving += _resolveHandler;
    }

    internal static WorkspaceAssemblyResolver Install(string entryAssemblyPath, SharedFrameworkCatalog sharedFrameworkCatalog)
        => new(entryAssemblyPath, sharedFrameworkCatalog);

    internal Assembly LoadEntryAssembly(string entryAssemblyPath)
        => AssemblyLoadContext.Default.LoadFromAssemblyPath(entryAssemblyPath);

    internal void PreloadDependencies(string entryAssemblyPath)
        => WorkspaceDependencyLoader.PreloadIntoDefaultContext(_dependencyResolver, entryAssemblyPath);

    internal bool IsOutputDirectoryAssembly(Assembly assembly)
    {
        if (string.IsNullOrEmpty(assembly.Location))
            return false;

        return assembly.Location.StartsWith(_outputDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName assemblyName)
    {
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
