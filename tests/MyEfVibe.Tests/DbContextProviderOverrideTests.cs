using MyEfVibe.Workspace;

namespace MyEfVibe.Tests;

public sealed class DbContextProviderOverrideTests
{
    [Fact]
    public void ResolveInstance_builds_npgsql_options_for_sqlserver_only_workspace()
    {
        var persistenceDll = FindPrebuiltPersistenceDll();

        if (persistenceDll is null)
        {
            return;
        }

        var outputDirectory = Path.GetDirectoryName(persistenceDll)!;
        using var host = TryLoadHost(persistenceDll, outputDirectory);

        if (host is null)
        {
            return;
        }

        var dbContext = DbContextActivator.ResolveInstance(
            host,
            "AdventureWorksDbContext",
            "Host=localhost;Port=5432;Database=adventureworks;Username=postgres;Password=Your_strong_Password123!",
            ProviderDescriptor.FromKnownProvider(MyEfVibeProvider.Npgsql),
            false);

        var providerName = dbContext.GetType()
            .GetProperty("Database")?
            .GetValue(dbContext)?
            .GetType()
            .GetProperty("ProviderName")?
            .GetValue(dbContext.GetType().GetProperty("Database")!.GetValue(dbContext)) as string;

        Assert.Contains("Npgsql", providerName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static WorkspaceHost? TryLoadHost(string persistenceDll, string outputDirectory)
    {
        var efProject = ResolveAdventureWorksEfProject();

        if (efProject is null)
        {
            return null;
        }

        var workspaceBuild = new WorkspaceBuildResult(
            Path.Combine(Path.GetTempPath(), "efvibe-tests", Guid.NewGuid().ToString("N")),
            efProject,
            efProject,
            outputDirectory,
            persistenceDll,
            "net10.0",
            new ProjectBuildOutput(outputDirectory));

        return WorkspaceHost.Load(workspaceBuild);
    }

    private static string? ResolveAdventureWorksEfProject()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("EFVIBE_ADVENTUREWORKS_EF_PROJECT");

        if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
        {
            return fromEnvironment;
        }

        foreach (var candidate in EnumerateAdventureWorksEfProjectCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateAdventureWorksEfProjectCandidates()
    {
        const string relativeProject =
            "apps/api-dotnet/src/AdventureWorks.Infrastructure.Persistence/AdventureWorks.Infrastructure.Persistence.csproj";

        var start = new DirectoryInfo(AppContext.BaseDirectory);

        for (var directory = start; directory is not null; directory = directory.Parent)
        {
            if (string.Equals(directory.Name, "my-ef-vibe", StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.Combine(directory.Parent!.FullName, "AdventureWorks", relativeProject);
            }

            yield return Path.Combine(directory.FullName, "AdventureWorks", relativeProject);
        }
    }

    private static string? FindPrebuiltPersistenceDll()
    {
        var root = Path.Combine(Path.GetTempPath(), "efvibe-integration");

        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(root, "AdventureWorks.Infrastructure.Persistence.dll", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal));
    }
}