namespace MyEfVibe.IntegrationTests;

internal static class IntegrationPrebuiltArtifacts
{
    private static readonly string[] RelationalScenarioIds = ["sqlserver", "postgresql", "oracle", "sqlite"];

    internal static bool TryFindRelationalBuildOutputs(
        out string persistenceDll,
        out string startupDll,
        out string persistenceOutputDirectory,
        out string startupOutputDirectory)
    {
        persistenceDll = string.Empty;
        startupDll = string.Empty;
        persistenceOutputDirectory = string.Empty;
        startupOutputDirectory = string.Empty;

        var root = Path.Combine(Path.GetTempPath(), "efvibe-integration");

        if (!Directory.Exists(root))
        {
            return false;
        }

        foreach (var scenarioId in RelationalScenarioIds)
        {
            var scenarioRoot = Path.Combine(root, scenarioId);

            if (!Directory.Exists(scenarioRoot))
            {
                continue;
            }

            foreach (var sessionRoot in Directory.EnumerateDirectories(scenarioRoot)
                         .OrderByDescending(Directory.GetLastWriteTimeUtc))
            {
                var persistenceCandidate = FindReleaseDll(sessionRoot, "AdventureWorks.Infrastructure.Persistence.dll", "net10.0");
                var startupCandidate = FindReleaseDll(sessionRoot, "AdventureWorks.API.dll", "net10.0");

                if (persistenceCandidate is null || startupCandidate is null)
                {
                    continue;
                }

                persistenceDll = persistenceCandidate;
                startupDll = startupCandidate;
                persistenceOutputDirectory = Path.GetDirectoryName(persistenceCandidate)!;
                startupOutputDirectory = Path.GetDirectoryName(startupCandidate)!;

                return true;
            }
        }

        return false;
    }

    private static string? FindReleaseDll(string searchRoot, string fileName, string framework)
    {
        return Directory
            .EnumerateFiles(searchRoot, fileName, SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => path.Contains(framework, StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
