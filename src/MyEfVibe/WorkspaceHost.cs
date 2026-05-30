using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis.Scripting.Hosting;

namespace MyEfVibe;

internal sealed class WorkspaceHost : IDisposable
{
    private readonly WorkspaceAssemblyResolver _resolver;
    private readonly string _targetFrameworkMoniker;
    private ProjectBuildOutput? _startupBuildOutput;
    private string? _startupOutputDirectory;
    private bool _startupBuildAttempted;

    private WorkspaceHost(
        WorkspaceAssemblyResolver resolver,
        InteractiveAssemblyLoader assemblyLoader,
        Assembly primaryAssembly,
        string projectPath,
        string startupProjectPath,
        string outputDirectory,
        string targetFrameworkMoniker,
        ProjectBuildOutput? startupBuildOutput,
        string? startupOutputDirectory,
        string sessionDirectory)
    {
        _resolver = resolver;
        AssemblyLoader = assemblyLoader;
        PrimaryAssembly = primaryAssembly;
        ProjectPath = projectPath;
        StartupProjectPath = startupProjectPath;
        OutputDirectory = outputDirectory;
        _targetFrameworkMoniker = targetFrameworkMoniker;
        _startupBuildOutput = startupBuildOutput;
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

    internal string TargetFrameworkMoniker => _targetFrameworkMoniker;

    internal string? StartupOutputDirectory => _startupOutputDirectory;

    internal string SessionDirectory { get; private set; }

    internal static WorkspaceHost Load(WorkspaceBuildResult workspaceBuild)
    {
        var startupAssemblyDll = ResolveStartupAssemblyDll(workspaceBuild);

        var sharedFrameworkCatalog = SharedFrameworkCatalog.Create(
            workspaceBuild.TargetFrameworkMoniker,
            workspaceBuild.OutputDirectory,
            workspaceBuild.PrimaryAssemblyDll,
            workspaceBuild.StartupOutputDirectory,
            startupAssemblyDll);

        WorkspaceSystemTextJsonBootstrap.PrimeSharedFramework(sharedFrameworkCatalog);

        var depsManifest = WorkspaceDepsManifest.TryLoad(workspaceBuild.PrimaryAssemblyDll);
        depsManifest = MergeStartupDepsManifest(workspaceBuild, depsManifest);

        WorkspaceSqliteNativeBootstrap.EnsureRegistered(depsManifest);

        var assemblyResolver =
            WorkspaceAssemblyResolver.Install(workspaceBuild.PrimaryAssemblyDll, sharedFrameworkCatalog, depsManifest);

        WorkspaceSystemTextJsonBootstrap.EnsureLoaded(assemblyResolver, sharedFrameworkCatalog);

        EnsureWorkspaceConfigurationManagerLoaded(assemblyResolver);

        // Preload EF packages first, then the entry assembly, then startup-only references.
        // Preloading the startup copy of the EF project DLL before LoadEntryAssembly causes
        // "Assembly with the same name is already loaded" (different paths, same simple name).
        assemblyResolver.PreloadCorePackages();

        var primaryAssembly = assemblyResolver.LoadEntryAssembly(workspaceBuild.PrimaryAssemblyDll);

        if (!string.IsNullOrEmpty(startupAssemblyDll))
        {
            assemblyResolver.PreloadStartupReferenceClosure(
                startupAssemblyDll,
                workspaceBuild.PrimaryAssemblyDll);
        }

        EnsureWorkspaceConfigurationManagerLoaded(assemblyResolver);

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
            workspaceBuild.TargetFrameworkMoniker,
            workspaceBuild.StartupBuildOutput,
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
            AssemblyResolutionHelpers.LoadFromPath(AssemblyLoadContext.Default, path);
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

    internal Assembly? LoadAssembly(AssemblyName assemblyName)
        => _resolver.ResolveAssembly(assemblyName);

    internal bool TryResolveAssemblyPath(string assemblySimpleName, out string absolutePath)
        => _resolver.TryResolveAssemblyPath(assemblySimpleName, out absolutePath);

    internal bool TryResolveAssemblyPath(AssemblyName assemblyName, out string absolutePath)
        => _resolver.TryResolveAssemblyPath(assemblyName, out absolutePath);

    internal void EnsureEntityFrameworkCoreLoaded()
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(static assembly =>
                string.Equals(assembly.GetName().Name, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal)))
            return;

        if (_resolver.DepsManifest?.TryResolve("Microsoft.EntityFrameworkCore.Abstractions", out var abstractionsPath) == true)
            LoadOrGetAssembly(abstractionsPath);

        if (_resolver.DepsManifest?.TryResolve("Microsoft.EntityFrameworkCore", out var corePath) == true)
        {
            PreloadExactReferencesFromAssembly(corePath);
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

    internal void EnsureEntityFrameworkRelationalLoaded()
    {
        EnsureEntityFrameworkCoreLoaded();

        if (AppDomain.CurrentDomain.GetAssemblies().Any(static assembly =>
                string.Equals(
                    assembly.GetName().Name,
                    "Microsoft.EntityFrameworkCore.Relational",
                    StringComparison.Ordinal)))
            return;

        if (_resolver.DepsManifest?.TryResolve("Microsoft.EntityFrameworkCore.Relational", out var relationalPath) == true)
        {
            PreloadPackageWithClosure(relationalPath);
            return;
        }

        var outputCandidate = Path.Combine(OutputDirectory, "Microsoft.EntityFrameworkCore.Relational.dll");

        if (File.Exists(outputCandidate))
            LoadOrGetAssembly(outputCandidate);
    }

    internal void EnsureProviderDependenciesLoaded(MyEfVibeProvider provider)
    {
        if (provider == MyEfVibeProvider.Sqlite)
            WorkspaceSqliteNativeBootstrap.EnsureBatteriesInitialized(this);
        else
            EnsureEntityFrameworkRelationalLoaded();

        foreach (var assemblySimpleName in ProviderAssemblyNames.For(provider))
            PreloadPackageByName(assemblySimpleName);
    }

    internal void PreloadPackageByName(string assemblySimpleName)
    {
        if (_resolver.DepsManifest?.TryResolve(assemblySimpleName, out var path) == true)
        {
            PreloadPackageWithClosure(path);
            return;
        }

        _ = LoadAssembly(assemblySimpleName);
    }

    private void PreloadPackageWithClosure(string assemblyPath)
    {
        if (_resolver.DepsManifest is null)
        {
            LoadOrGetAssembly(assemblyPath);
            return;
        }

        WorkspaceDependencyLoader.PreloadAssemblyReferenceClosure(
            AssemblyLoadContext.Default,
            _resolver.DepsManifest,
            assemblyPath);

        LoadOrGetAssembly(assemblyPath);
    }

    internal void EnsureAspNetCoreSharedFrameworkLoaded()
    {
        foreach (var assemblySimpleName in new[]
                 {
                     "Microsoft.AspNetCore.Mvc.Core",
                     "Microsoft.AspNetCore.Routing",
                     "Microsoft.AspNetCore.Http.Abstractions",
                     "Microsoft.AspNetCore.OpenApi",
                 })
        {
            _ = LoadAssembly(assemblySimpleName);
        }
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

        _startupBuildOutput ??= WorkspaceBuilder.GetIsolatedBuildOutput(
            SessionDirectory,
            StartupProjectPath,
            _targetFrameworkMoniker);

        if (WorkspaceBuildResult.TryLocateStartupOutput(
                StartupProjectPath,
                _targetFrameworkMoniker,
                _startupBuildOutput,
                out var startupOutputDirectory))
        {
            _startupOutputDirectory = startupOutputDirectory;
            return _startupOutputDirectory;
        }

        var startupFramework = ProjectTargetFrameworkResolver.ResolveBuildFramework(StartupProjectPath, null);
        _startupBuildOutput = WorkspaceBuilder.GetIsolatedBuildOutput(
            SessionDirectory,
            StartupProjectPath,
            startupFramework);
        WorkspaceBuilder.RunDotnetBuild(StartupProjectPath, startupFramework, _startupBuildOutput);

        if (WorkspaceBuildResult.TryLocateStartupOutput(
                StartupProjectPath,
                startupFramework,
                _startupBuildOutput,
                out startupOutputDirectory))
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

        foreach (var dllPath in _resolver.DepsManifest.EnumerateProjectAssemblyPaths())
        {
            if (!File.Exists(dllPath))
                continue;

            if (!WorkspaceAssemblyFilter.ShouldScanAssembly(dllPath))
                continue;

            if (seen.Add(dllPath))
                yield return dllPath;
        }
    }

    private static string? ResolveStartupAssemblyDll(WorkspaceBuildResult workspaceBuild)
    {
        if (string.IsNullOrEmpty(workspaceBuild.StartupOutputDirectory)
            || string.Equals(
                workspaceBuild.OutputDirectory,
                workspaceBuild.StartupOutputDirectory,
                StringComparison.OrdinalIgnoreCase))
            return null;

        var startupAssemblyName = CsprojReader.ReadLogicalAssemblyName(workspaceBuild.StartupProjectPath);
        var startupDll = Path.Combine(workspaceBuild.StartupOutputDirectory, $"{startupAssemblyName}.dll");

        return File.Exists(startupDll) ? startupDll : null;
    }

    private static WorkspaceDepsManifest? MergeStartupDepsManifest(
        WorkspaceBuildResult workspaceBuild,
        WorkspaceDepsManifest? depsManifest)
    {
        if (string.IsNullOrEmpty(workspaceBuild.StartupOutputDirectory)
            || string.Equals(
                workspaceBuild.OutputDirectory,
                workspaceBuild.StartupOutputDirectory,
                StringComparison.OrdinalIgnoreCase))
            return depsManifest;

        var startupDll = ResolveStartupAssemblyDll(workspaceBuild);

        if (startupDll is null)
            return depsManifest;

        return WorkspaceDepsManifest.Merge(depsManifest, WorkspaceDepsManifest.TryLoad(startupDll));
    }

    private static IEnumerable<string> CollectStartupRegistrationAssemblyPaths(
        string primaryAssemblyDll,
        WorkspaceDepsManifest? depsManifest)
    {
        var paths = new List<string> { primaryAssemblyDll };
        var seenSimpleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AssemblyName.GetAssemblyName(primaryAssemblyDll).Name ?? string.Empty,
        };

        if (depsManifest is null)
            return paths;

        foreach (var projectPath in depsManifest.EnumerateProjectAssemblyPaths())
        {
            if (!File.Exists(projectPath))
                continue;

            string simpleName;

            try
            {
                simpleName = AssemblyName.GetAssemblyName(projectPath).Name ?? projectPath;
            }
            catch (BadImageFormatException)
            {
                continue;
            }
            catch (FileLoadException)
            {
                continue;
            }

            if (!seenSimpleNames.Add(simpleName))
                continue;

            paths.Add(projectPath);
        }

        return paths;
    }

    private void PreloadExactReferencesFromAssembly(string assemblyPath)
    {
        if (_resolver.DepsManifest is null)
            return;

        foreach (var reference in AssemblyReferenceReader.Read(assemblyPath))
        {
            if (string.IsNullOrEmpty(reference.Name))
                continue;

            if (AssemblyResolutionHelpers.FindLoadedAssembly(reference) is not null)
                continue;

            if (!_resolver.DepsManifest.TryResolve(reference, out var referencePath))
                continue;

            try
            {
                LoadOrGetAssembly(referencePath);
            }
            catch (BadImageFormatException)
            {
            }
            catch (FileLoadException)
            {
            }
        }
    }

    private static Assembly LoadOrGetAssembly(string absolutePath)
        => AssemblyResolutionHelpers.LoadFromPath(AssemblyLoadContext.Default, absolutePath);

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
