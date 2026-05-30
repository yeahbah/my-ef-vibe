namespace MyEfVibe.Tests;

public sealed class ConfigurationManagerPreloadTests
{
    [Fact]
    public void WorkspaceHost_loads_configuration_manager_before_sqlclient_for_postgresql_override()
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
            "Host=localhost;Port=5432;Database=adventureworks;Username=postgres;Password=x",
            MyEfVibeProvider.Npgsql,
            allowInteractiveSelection: false);

        Assert.Equal("AdventureWorksDbContext", dbContext.GetType().Name);
    }

    private static string? FindPrebuiltPersistenceDll() =>
        FindPrebuiltDll("AdventureWorks.Infrastructure.Persistence.dll");

    private static string? FindPrebuiltStartupDll() =>
        FindPrebuiltDll("AdventureWorks.API.dll");

    private static string? FindPrebuiltDll(string fileName)
    {
        var root = Path.Combine(Path.GetTempPath(), "efvibe-integration");

        if (!Directory.Exists(root))
            return null;

        return Directory
            .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
