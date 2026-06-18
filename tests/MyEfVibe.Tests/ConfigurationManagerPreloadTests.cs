using MyEfVibe.Workspace;

namespace MyEfVibe.Tests;

public sealed class ConfigurationManagerPreloadTests
{
    [Fact]
    public void WorkspaceHost_loads_configuration_manager_before_sqlclient_for_postgresql_override()
    {
        var persistenceDll = FindPrebuiltPersistenceDll();
        var startupDll = FindPrebuiltStartupDll();

        if (persistenceDll is null || startupDll is null)
        {
            return;
        }

        var outputDirectory = Path.GetDirectoryName(persistenceDll)!;
        var startupOutputDirectory = Path.GetDirectoryName(startupDll)!;
        var efProject =
            "/home/adiaz/Projects/AdventureWorks/apps/api-dotnet/src/AdventureWorks.Infrastructure.Persistence/AdventureWorks.Infrastructure.Persistence.csproj";
        var startupProject =
            "/home/adiaz/Projects/AdventureWorks/apps/api-dotnet/src/AdventureWorks.API/AdventureWorks.API.csproj";

        if (!File.Exists(efProject))
        {
            return;
        }

        var workspaceBuild = new WorkspaceBuildResult(
            Path.Combine(Path.GetTempPath(), "efvibe-tests", Guid.NewGuid().ToString("N")),
            efProject,
            startupProject,
            outputDirectory,
            persistenceDll,
            "net10.0",
            new ProjectBuildOutput(outputDirectory),
            StartupOutputDirectory: startupOutputDirectory);

        using var host = WorkspaceHost.Load(workspaceBuild);

        var dbContext = DbContextActivator.ResolveInstance(
            host,
            "AdventureWorksDbContext",
            "Host=localhost;Port=5432;Database=adventureworks;Username=postgres;Password=x",
            ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.Npgsql),
            false);

        Assert.Equal("AdventureWorksDbContext", dbContext.GetType().Name);
    }

    private static string? FindPrebuiltPersistenceDll()
    {
        return FindPrebuiltDll("AdventureWorks.Infrastructure.Persistence.dll");
    }

    private static string? FindPrebuiltStartupDll()
    {
        return FindPrebuiltDll("AdventureWorks.API.dll");
    }

    private static string? FindPrebuiltDll(string fileName)
    {
        var root = Path.Combine(Path.GetTempPath(), "efvibe-integration");

        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal));
    }
}