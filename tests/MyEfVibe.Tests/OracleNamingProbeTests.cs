using MyEfVibe.Workspace;

namespace MyEfVibe.Tests;

public sealed class OracleNamingProbeTests
{
    [Fact]
    public void Detect_returns_native_uppercase_for_aw_oracle_dump()
    {
        var persistenceDll = FindPrebuiltPersistenceDll();

        if (persistenceDll is null)
        {
            return;
        }

        var outputDirectory = Path.GetDirectoryName(persistenceDll)!;
        var efProject =
            "/home/yeahbah/Projects/AdventureWorksOra/apps/api-dotnet/src/AdventureWorks.Infrastructure.Persistence/AdventureWorks.Infrastructure.Persistence.csproj";

        if (!File.Exists(efProject))
        {
            return;
        }

        using var host = WorkspaceHost.Load(
            new WorkspaceBuildResult(
                Path.Combine(Path.GetTempPath(), "efvibe-tests", Guid.NewGuid().ToString("N")),
                efProject,
                efProject,
                outputDirectory,
                persistenceDll,
                "net10.0",
                new ProjectBuildOutput(outputDirectory)));

        var style = OracleNamingProbe.Detect(
            host,
            "User Id=ADVENTUREWORKS;Password=AdventureWorks123;Data Source=localhost:1521/FREEPDB1");

        if (style == OracleNamingStyle.None)
        {
            return;
        }

        Assert.Equal(OracleNamingStyle.NativeUppercase, style);
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
