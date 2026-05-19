using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis.Scripting.Hosting;

namespace MyEfVibe;

internal sealed class WorkspaceHost : IDisposable
{
    private readonly WorkspaceAssemblyResolver _resolver;
    private string? _startupOutputDirectory;
    private bool _startupBuildAttempted;

    private WorkspaceHost(
        WorkspaceAssemblyResolver resolver,
        InteractiveAssemblyLoader assemblyLoader,
        Assembly primaryAssembly,
        string projectPath,
        string startupProjectPath,
        string outputDirectory,
        string? startupOutputDirectory,
        string sessionDirectory)
    {
        _resolver = resolver;
        AssemblyLoader = assemblyLoader;
        PrimaryAssembly = primaryAssembly;
        ProjectPath = projectPath;
        StartupProjectPath = startupProjectPath;
        OutputDirectory = outputDirectory;
        _startupOutputDirectory = startupOutputDirectory;
        SessionDirectory = sessionDirectory;
    }

    internal void SetSessionDirectory(string sessionDirectory) =>
        SessionDirectory = SessionPaths.EnsureSessionDirectory(sessionDirectory);

    internal InteractiveAssemblyLoader AssemblyLoader { get; }

    internal Assembly PrimaryAssembly { get; }

    internal string ProjectPath { get; }

    internal string StartupProjectPath { get; }

    internal string OutputDirectory { get; }

    internal string? StartupOutputDirectory => _startupOutputDirectory;

    internal string SessionDirectory { get; private set; }

    internal static WorkspaceHost Load(WorkspaceBuildResult workspaceBuild)
    {
        var sharedFrameworkCatalog =
            SharedFrameworkCatalog.Load(workspaceBuild.OutputDirectory, workspaceBuild.PrimaryAssemblyDll);

        var depsManifest = WorkspaceDepsManifest.TryLoad(workspaceBuild.PrimaryAssemblyDll);

        var assemblyResolver =
            WorkspaceAssemblyResolver.Install(workspaceBuild.PrimaryAssemblyDll, sharedFrameworkCatalog, depsManifest);

        assemblyResolver.PreloadDependencies(workspaceBuild.PrimaryAssemblyDll);

        EnsureWorkspaceConfigurationManagerLoaded(assemblyResolver);

        var primaryAssembly = assemblyResolver.LoadEntryAssembly(workspaceBuild.PrimaryAssemblyDll);

        var assemblyLoader = new InteractiveAssemblyLoader();

        assemblyLoader.RegisterDependency(primaryAssembly);

        // Register only project-built assemblies at startup. Copied NuGet DLLs in bin/ (e.g.
        // DiagnosticSource 10) are resolved on demand with version-aware .deps.json handling.
        foreach (var referencePath in CollectStartupRegistrationAssemblyPaths(
                     workspaceBuild.PrimaryAssemblyDll,
                     depsManifest))
        {
            if (!File.Exists(referencePath))
                continue;

            try
            {
                var loaded = LoadOrGetAssembly(referencePath);

                assemblyLoader.RegisterDependency(loaded);
            }
            catch (BadImageFormatException)
            {
            }
            catch (FileLoadException)
            {
            }
        }

        return new WorkspaceHost(
            assemblyResolver,
            assemblyLoader,
            primaryAssembly,
            workspaceBuild.ProjectPath,
            workspaceBuild.StartupProjectPath,
            workspaceBuild.OutputDirectory,
            workspaceBuild.StartupOutputDirectory,
            workspaceBuild.SessionDirectory);
    }

    private static void EnsureWorkspaceConfigurationManagerLoaded(WorkspaceAssemblyResolver resolver)
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(static assembly =>
                string.Equals(
                    assembly.GetName().Name,
                    "System.Configuration.ConfigurationManager",
                    StringComparison.OrdinalIgnoreCase)))
            return;

        if (resolver.DepsManifest?.TryResolve("System.Configuration.ConfigurationManager", out var path) != true)
            return;

        try
        {
            AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        }
        catch (Exception failure)
        {
            throw new InvalidOperationException(
                "Failed to load `System.Configuration.ConfigurationManager` required by Microsoft.Data.SqlClient."
                + $"{Environment.NewLine}Path: {path}"
                + $"{Environment.NewLine}{failure.Message}",
                failure);
        }
    }

    internal Assembly? LoadAssembly(string assemblySimpleName)
        => _resolver.ResolveAssembly(assemblySimpleName);

    internal void EnsureEntityFrameworkCoreLoaded()
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(static assembly =>
                string.Equals(assembly.GetName().Name, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal)))
            return;

        if (_resolver.DepsManifest?.TryResolve("Microsoft.EntityFrameworkCore.Abstractions", out var abstractionsPath) == true)
            LoadOrGetAssembly(abstractionsPath);

        if (_resolver.DepsManifest?.TryResolve("Microsoft.EntityFrameworkCore", out var corePath) == true)
        {
            LoadOrGetAssembly(corePath);
            return;
        }

        var outputCandidate = Path.Combine(OutputDirectory, "Microsoft.EntityFrameworkCore.dll");

        if (File.Exists(outputCandidate))
        {
            LoadOrGetAssembly(outputCandidate);
            return;
        }

        throw new InvalidOperationException(
            "Could not load `Microsoft.EntityFrameworkCore` for the built project."
            + $"{Environment.NewLine}Ensure the project references EF Core and was built successfully,"
            + $" or use the API host project with `-p` so dependencies are available.");
    }

    internal IEnumerable<Assembly> EnumerateDiscoveryAssemblies()
        => EnumerateAssembliesFromPaths(EnumerateEfDiscoveryAssemblyPaths());

    internal IEnumerable<Assembly> EnumerateDesignTimeDiscoveryAssemblies()
        => EnumerateAssembliesFromPaths(EnumerateDesignTimeDiscoveryAssemblyPaths());

    private IEnumerable<Assembly> EnumerateAssembliesFromPaths(IEnumerable<string> assemblyPaths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assemblyPath in assemblyPaths)
        {
            Assembly assembly;

            try
            {
                assembly = LoadOrGetAssembly(assemblyPath);
            }
            catch (BadImageFormatException)
            {
                continue;
            }
            catch (FileLoadException)
            {
                continue;
            }

            var identity = assembly.FullName ?? assembly.GetName().Name ?? assemblyPath;

            if (!seen.Add(identity))
                continue;

            if (IsToolingAssembly(assembly.GetName().Name))
                continue;

            yield return assembly;
        }
    }

    private IEnumerable<string> EnumerateEfDiscoveryAssemblyPaths()
        => EnumerateDiscoveryAssemblyPaths(OutputDirectory);

    private IEnumerable<string> EnumerateDesignTimeDiscoveryAssemblyPaths()
    {
        foreach (var path in EnumerateEfDiscoveryAssemblyPaths())
            yield return path;

        var startupOutputDirectory = EnsureStartupOutputDirectory();

        if (string.IsNullOrEmpty(startupOutputDirectory)
            || string.Equals(startupOutputDirectory, OutputDirectory, StringComparison.OrdinalIgnoreCase))
            yield break;

        foreach (var path in EnumerateDiscoveryAssemblyPaths(startupOutputDirectory))
            yield return path;
    }

    private string? EnsureStartupOutputDirectory()
    {
        if (!string.IsNullOrEmpty(_startupOutputDirectory))
            return _startupOutputDirectory;

        if (string.Equals(ProjectPath, StartupProjectPath, StringComparison.OrdinalIgnoreCase))
            return _startupOutputDirectory = OutputDirectory;

        if (_startupBuildAttempted)
            return null;

        _startupBuildAttempted = true;

        if (WorkspaceBuildResult.TryLocateStartupOutput(StartupProjectPath, out var startupOutputDirectory))
        {
            _startupOutputDirectory = startupOutputDirectory;
            return _startupOutputDirectory;
        }

        WorkspaceBuilder.RunDotnetBuild(StartupProjectPath);

        if (WorkspaceBuildResult.TryLocateStartupOutput(StartupProjectPath, out startupOutputDirectory))
            _startupOutputDirectory = startupOutputDirectory;

        return _startupOutputDirectory;
    }

    private IEnumerable<string> EnumerateDiscoveryAssemblyPaths(string outputDirectory)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(outputDirectory))
            yield break;

        foreach (var dllPath in Directory.EnumerateFiles(outputDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            if (!WorkspaceAssemblyFilter.ShouldScanAssembly(dllPath))
                continue;

            if (seen.Add(dllPath))
                yield return dllPath;
        }

        if (_resolver.DepsManifest is null)
            yield break;

        foreach (var dllPath in _resolver.DepsManifest.RuntimeAssemblyPaths)
        {
            if (!File.Exists(dllPath))
                continue;

            if (!WorkspaceAssemblyFilter.ShouldScanAssembly(dllPath))
                continue;

            if (seen.Add(dllPath))
                yield return dllPath;
        }
    }

    private static IEnumerable<string> CollectStartupRegistrationAssemblyPaths(
        string primaryAssemblyDll,
        WorkspaceDepsManifest? depsManifest)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { primaryAssemblyDll };

        if (depsManifest is null)
            return paths;

        foreach (var projectPath in depsManifest.EnumerateProjectAssemblyPaths())
            paths.Add(projectPath);

        return paths;
    }

    private static Assembly LoadOrGetAssembly(string absolutePath)
    {
        var assemblyName = AssemblyName.GetAssemblyName(absolutePath);

        return AssemblyResolutionHelpers.FindLoadedAssembly(assemblyName)
            ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(absolutePath);
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
