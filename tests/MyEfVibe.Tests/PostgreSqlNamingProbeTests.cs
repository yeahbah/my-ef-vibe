using MyEfVibe.Workspace;

namespace MyEfVibe.Tests;

public sealed class PostgreSqlNamingProbeTests
{
    [Fact]
    public void Detect_returns_adventureworks_pascal_case_for_adventureworks_postgres()
    {
        var persistenceDll = FindPrebuiltPersistenceDll();

        if (persistenceDll is null)
        {
            return;
        }

        var outputDirectory = Path.GetDirectoryName(persistenceDll)!;
        var efProject =
            "/home/yeahbah/Projects/AdventureWorksPg/apps/api-dotnet/src/AdventureWorks.Infrastructure.Persistence/AdventureWorks.Infrastructure.Persistence.csproj";

        if (!File.Exists(efProject))
        {
            return;
        }

        var workspaceBuild = new WorkspaceBuildResult(
            Path.Combine(Path.GetTempPath(), "efvibe-tests", Guid.NewGuid().ToString("N")),
            efProject,
            efProject,
            outputDirectory,
            persistenceDll,
            "net10.0",
            new ProjectBuildOutput(outputDirectory));

        using var host = WorkspaceHost.Load(workspaceBuild);

        var connectionString =
            "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=AdventureWorks_Dev_2026!;Timeout=3";

        var style = PostgreSqlNamingProbe.Detect(host, connectionString);

        if (style == PostgreSqlNamingStyle.None)
        {
            return;
        }

        Assert.False(PostgreSqlNamingProbe.RequiresLowercaseMapping(host, connectionString));
        Assert.Equal(PostgreSqlNamingStyle.AdventureWorksPascalCase, style);
    }

    private static string? FindPrebuiltPersistenceDll()
    {
        var candidates = new[]
        {
            Path.Combine(
                "/home/yeahbah/Projects/AdventureWorksPg/apps/api-dotnet/src/AdventureWorks.Infrastructure.Persistence/bin",
                "Debug",
                "net10.0",
                "AdventureWorks.Infrastructure.Persistence.dll"),
            Path.Combine(
                "/home/yeahbah/Projects/AdventureWorksPg/apps/api-dotnet/src/AdventureWorks.Infrastructure.Persistence/bin",
                "Release",
                "net10.0",
                "AdventureWorks.Infrastructure.Persistence.dll")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
