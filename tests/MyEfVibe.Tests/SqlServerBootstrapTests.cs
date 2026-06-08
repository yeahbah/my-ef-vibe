namespace MyEfVibe.Tests;

public sealed class SqlServerBootstrapTests
{
    [Fact]
    public void ResolveInstance_builds_sqlserver_options_for_adventureworks_workspace()
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
            "/home/yeahbah/Projects/AdventureWorks-with-ef-vibe/apps/api-dotnet/src/AdventureWorks.Infrastructure.Persistence/AdventureWorks.Infrastructure.Persistence.csproj";
        var startupProject =
            "/home/yeahbah/Projects/AdventureWorks-with-ef-vibe/apps/api-dotnet/src/AdventureWorks.API/AdventureWorks.API.csproj";

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
            "Server=localhost,1433;Database=AdventureWorks2022;User Id=sa;Password=test;TrustServerCertificate=true",
            ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.SqlServer),
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
        foreach (var root in new[]
                 {
                     "/home/yeahbah/Projects/AdventureWorks-with-ef-vibe/apps/api-dotnet/src",
                     Path.Combine(Path.GetTempPath(), "efvibe-integration")
                 })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var match = Directory
                .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
                .FirstOrDefault(path =>
                    path.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal));

            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
