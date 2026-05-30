namespace MyEfVibe.Tests;

public sealed class SqliteBootstrapTests
{
    [Fact]
    public void EnsureBatteriesInitialized_sets_provider_before_data_sqlite_loads()
    {
        var persistenceDll = FindPrebuiltPersistenceDll();
        var startupDll = FindPrebuiltStartupDll();

        if (persistenceDll is null || startupDll is null)
            return;

        var outputDirectory = Path.GetDirectoryName(persistenceDll)!;
        var startupOutputDirectory = Path.GetDirectoryName(startupDll)!;
        var efProject = "/home/adiaz/Projects/AdventureWorks/apps/api-dotnet/src/AdventureWorks.Infrastructure.Persistence/AdventureWorks.Infrastructure.Persistence.csproj";
        var startupProject = "/home/adiaz/Projects/AdventureWorks/apps/api-dotnet/src/AdventureWorks.API/AdventureWorks.API.csproj";

        if (!File.Exists(efProject))
            return;

        var workspaceBuild = new WorkspaceBuildResult(
            SessionDirectory: Path.Combine(Path.GetTempPath(), "efvibe-tests", Guid.NewGuid().ToString("N")),
            ProjectPath: efProject,
            StartupProjectPath: startupProject,
            OutputDirectory: outputDirectory,
            PrimaryAssemblyDll: persistenceDll,
            TargetFrameworkMoniker: "net10.0",
            ProjectBuildOutput: new ProjectBuildOutput(outputDirectory),
            StartupOutputDirectory: startupOutputDirectory);

        using var host = WorkspaceHost.Load(workspaceBuild);

        host.EnsureProviderDependenciesLoaded(MyEfVibeProvider.Sqlite);

        Assert.NotNull(host.LoadAssembly("SQLitePCLRaw.core"));
        Assert.NotNull(host.LoadAssembly("SQLitePCLRaw.provider.e_sqlite3"));
        Assert.NotNull(host.LoadAssembly("Microsoft.EntityFrameworkCore.Sqlite"));
        Assert.NotNull(host.LoadAssembly("Microsoft.Data.Sqlite"));

        var dbContext = DbContextActivator.ResolveInstance(
            host,
            "AdventureWorksDbContext",
            "Data Source=/home/adiaz/Projects/AdventureWorks/Source/AdventureWorksLT.db",
            MyEfVibeProvider.Sqlite,
            allowInteractiveSelection: false);

        _ = dbContext.GetType().GetProperty("Database")!.GetValue(dbContext);
    }

    [Fact]
    public void ResolveInstance_without_explicit_preload_can_read_provider_name()
    {
        var persistenceDll = FindPrebuiltPersistenceDll();
        var startupDll = FindPrebuiltStartupDll();

        if (persistenceDll is null || startupDll is null)
            return;

        var outputDirectory = Path.GetDirectoryName(persistenceDll)!;
        var startupOutputDirectory = Path.GetDirectoryName(startupDll)!;
        var efProject = "/home/adiaz/Projects/AdventureWorks/apps/api-dotnet/src/AdventureWorks.Infrastructure.Persistence/AdventureWorks.Infrastructure.Persistence.csproj";
        var startupProject = "/home/adiaz/Projects/AdventureWorks/apps/api-dotnet/src/AdventureWorks.API/AdventureWorks.API.csproj";

        if (!File.Exists(efProject))
            return;

        var workspaceBuild = new WorkspaceBuildResult(
            SessionDirectory: Path.Combine(Path.GetTempPath(), "efvibe-tests", Guid.NewGuid().ToString("N")),
            ProjectPath: efProject,
            StartupProjectPath: startupProject,
            OutputDirectory: outputDirectory,
            PrimaryAssemblyDll: persistenceDll,
            TargetFrameworkMoniker: "net10.0",
            ProjectBuildOutput: new ProjectBuildOutput(outputDirectory),
            StartupOutputDirectory: startupOutputDirectory);

        using var host = WorkspaceHost.Load(workspaceBuild);

        var dbContext = DbContextActivator.ResolveInstance(
            host,
            "AdventureWorksDbContext",
            "Data Source=/home/adiaz/Projects/AdventureWorks/Source/AdventureWorksLT.db",
            MyEfVibeProvider.Sqlite,
            allowInteractiveSelection: false);

        var database = dbContext.GetType().GetProperty("Database")!.GetValue(dbContext);
        var providerName = database!.GetType().GetProperty("ProviderName")!.GetValue(database) as string;

        Assert.Contains("Sqlite", providerName, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindPrebuiltPersistenceDll()
    {
        var root = Path.Combine(Path.GetTempPath(), "efvibe-integration");

        if (!Directory.Exists(root))
            return null;

        return Directory
            .EnumerateFiles(root, "AdventureWorks.Infrastructure.Persistence.dll", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static string? FindPrebuiltStartupDll()
    {
        var root = Path.Combine(Path.GetTempPath(), "efvibe-integration");

        if (!Directory.Exists(root))
            return null;

        return Directory
            .EnumerateFiles(root, "AdventureWorks.API.dll", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
