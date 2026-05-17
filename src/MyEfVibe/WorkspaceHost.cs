using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis.Scripting.Hosting;

namespace MyEfVibe;

internal sealed class WorkspaceHost : IDisposable
{
    private readonly WorkspaceAssemblyResolver _resolver;

    private WorkspaceHost(
        WorkspaceAssemblyResolver resolver,
        InteractiveAssemblyLoader assemblyLoader,
        Assembly primaryAssembly,
        string outputDirectory)
    {
        _resolver = resolver;
        AssemblyLoader = assemblyLoader;
        PrimaryAssembly = primaryAssembly;
        OutputDirectory = outputDirectory;
    }

    internal InteractiveAssemblyLoader AssemblyLoader { get; }

    internal Assembly PrimaryAssembly { get; }

    internal string OutputDirectory { get; }

    internal static WorkspaceHost Load(WorkspaceBuildResult workspaceBuild)
    {
        var sharedFrameworkCatalog =
            SharedFrameworkCatalog.Load(workspaceBuild.OutputDirectory, workspaceBuild.PrimaryAssemblyDll);

        var assemblyResolver =
            WorkspaceAssemblyResolver.Install(workspaceBuild.PrimaryAssemblyDll, sharedFrameworkCatalog);

        assemblyResolver.PreloadDependencies(workspaceBuild.PrimaryAssemblyDll);

        var primaryAssembly = assemblyResolver.LoadEntryAssembly(workspaceBuild.PrimaryAssemblyDll);

        var assemblyLoader = new InteractiveAssemblyLoader();

        assemblyLoader.RegisterDependency(primaryAssembly);

        foreach (var referencePath in workspaceBuild.ReferenceAssemblyPaths)
        {
            if (!File.Exists(referencePath))
                continue;

            try
            {
                var loaded = AssemblyLoadContext.Default.LoadFromAssemblyPath(referencePath);

                assemblyLoader.RegisterDependency(loaded);
            }
            catch (BadImageFormatException)
            {
            }
            catch (FileLoadException)
            {
            }
        }

        return new WorkspaceHost(assemblyResolver, assemblyLoader, primaryAssembly, workspaceBuild.OutputDirectory);
    }

    internal IEnumerable<Assembly> EnumerateLoadedAssemblies()
        => AppDomain.CurrentDomain.GetAssemblies();

    internal IEnumerable<Assembly> EnumerateApplicationAssemblies()
    {
        yield return PrimaryAssembly;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ReferenceEquals(assembly, PrimaryAssembly))
                continue;

            if (!_resolver.IsOutputDirectoryAssembly(assembly))
                continue;

            if (IsToolingAssembly(assembly.GetName().Name))
                continue;

            yield return assembly;
        }
    }

    private static bool IsToolingAssembly(string? assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return true;

        return assemblyName.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)
               || assemblyName.StartsWith("Microsoft.Build", StringComparison.OrdinalIgnoreCase)
               || assemblyName.StartsWith("Microsoft.EntityFrameworkCore.Design", StringComparison.OrdinalIgnoreCase)
               || assemblyName.StartsWith("Humanizer", StringComparison.OrdinalIgnoreCase)
               || assemblyName.StartsWith("Mono.TextTemplating", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        AssemblyLoader.Dispose();
        _resolver.Dispose();
    }
}
