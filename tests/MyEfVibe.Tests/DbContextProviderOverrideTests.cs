namespace MyEfVibe.Tests;

public sealed class DbContextProviderOverrideTests
{
    [Fact]
    public void ResolveInstance_builds_npgsql_options_for_sqlserver_only_workspace()
    {
        var persistenceDll = FindPrebuiltPersistenceDll();

        if (persistenceDll is null)
            return;

        var outputDirectory = Path.GetDirectoryName(persistenceDll)!;
        using var host = LoadHost(persistenceDll, outputDirectory);

        var dbContext = DbContextActivator.ResolveInstance(
            host,
            "AdventureWorksDbContext",
            "Host=localhost;Port=5432;Database=adventureworks;Username=postgres;Password=Your_strong_Password123!",
            MyEfVibeProvider.Npgsql,
            allowInteractiveSelection: false);

        var providerName = dbContext.GetType()
            .GetProperty("Database")?
            .GetValue(dbContext)?
            .GetType()
            .GetProperty("ProviderName")?
            .GetValue(dbContext.GetType().GetProperty("Database")!.GetValue(dbContext)) as string;

        Assert.Contains("Npgsql", providerName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static WorkspaceHost LoadHost(string persistenceDll, string outputDirectory)
    {
        var efProject = "/home/adiaz/Projects/AdventureWorks/apps/api-dotnet/src/AdventureWorks.Infrastructure.Persistence/AdventureWorks.Infrastructure.Persistence.csproj";

        if (!File.Exists(efProject))
            throw new InvalidOperationException($"AdventureWorks EF project not found: {efProject}");

        var workspaceBuild = new WorkspaceBuildResult(
            SessionDirectory: Path.Combine(Path.GetTempPath(), "efvibe-tests", Guid.NewGuid().ToString("N")),
            ProjectPath: efProject,
            StartupProjectPath: efProject,
            OutputDirectory: outputDirectory,
            PrimaryAssemblyDll: persistenceDll,
            TargetFrameworkMoniker: "net10.0",
            ProjectBuildOutput: new ProjectBuildOutput(outputDirectory));

        return WorkspaceHost.Load(workspaceBuild);
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
}
