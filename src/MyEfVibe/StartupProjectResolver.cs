namespace MyEfVibe;

internal static class StartupProjectResolver
{
    private const int AutoPickScoreGap = 12;

    internal sealed record RankedStartup(string CsprojPath, int Score, string Label);

    internal static FileInfo Resolve(
        string searchDirectory,
        FileInfo efProject,
        string? explicitStartupPathOrNull)
    {
        if (!string.IsNullOrWhiteSpace(explicitStartupPathOrNull))
            return ProjectPathResolver.ResolveCsproj(explicitStartupPathOrNull, searchDirectory);

        var inferred = TryInfer(searchDirectory, efProject.FullName);

        return inferred ?? efProject;
    }

    private static FileInfo? TryInfer(string searchDirectory, string efProjectPath)
    {
        var normalizedSearch = Path.GetFullPath(searchDirectory.TrimEnd(Path.DirectorySeparatorChar));

        if (!Directory.Exists(normalizedSearch))
            return null;

        var referencers = Directory
            .EnumerateFiles(normalizedSearch, "*.csproj", SearchOption.AllDirectories)
            .Where(static path => !IsUnderBuildArtifacts(path))
            .Where(path => !string.Equals(path, efProjectPath, StringComparison.OrdinalIgnoreCase))
            .Where(path => !CsprojInspector.IsTestProject(path))
            .Where(path => ProjectReferenceWalker.ReferencesProject(path, efProjectPath))
            .Select(path => new RankedStartup(path, ScoreStartupCandidate(path), BuildLabel(path)))
            .Where(static candidate => candidate.Score > 0)
            .OrderByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.CsprojPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (referencers.Length == 0)
            return null;

        if (referencers.Length == 1
            || referencers[0].Score - referencers[1].Score >= AutoPickScoreGap
            || !InteractiveSelection.CanPrompt)
            return new FileInfo(referencers[0].CsprojPath);

        var choice = InteractiveSelection.Choose(
            "[bold]Multiple startup projects reference the EF project. Which has configuration (user secrets / appsettings)?[/]",
            referencers.Select(candidate => new SelectionOption<string>(
                candidate.CsprojPath,
                $"{candidate.Label} [dim](score {candidate.Score})[/]")).ToArray());

        return new FileInfo(choice);
    }

    private static int ScoreStartupCandidate(string csprojPath)
    {
        var score = 0;
        var projectDirectory = Path.GetDirectoryName(csprojPath)!;

        if (CsprojInspector.TryGetUserSecretsId(csprojPath, out _))
            score += 50;

        if (File.Exists(Path.Combine(projectDirectory, "appsettings.json")))
            score += 30;

        if (File.Exists(Path.Combine(projectDirectory, "appsettings.Development.json")))
            score += 10;

        if (CsprojInspector.IsExecutableOutput(csprojPath))
            score += 25;

        if (CsprojInspector.UsesWebSdk(csprojPath))
            score += 30;

        return score;
    }

    private static string BuildLabel(string csprojPath)
    {
        var traits = new List<string>();
        var projectDirectory = Path.GetDirectoryName(csprojPath)!;

        if (CsprojInspector.TryGetUserSecretsId(csprojPath, out _))
            traits.Add("user secrets");

        if (File.Exists(Path.Combine(projectDirectory, "appsettings.json")))
            traits.Add("appsettings");

        if (CsprojInspector.IsExecutableOutput(csprojPath))
            traits.Add("executable");

        if (CsprojInspector.UsesWebSdk(csprojPath))
            traits.Add("web");

        var name = Path.GetFileName(csprojPath);

        return traits.Count == 0
            ? name
            : $"{name} [grey]({string.Join(", ", traits)})[/]";
    }

    private static bool IsUnderBuildArtifacts(string absolutePathCandidate)
        =>
        absolutePathCandidate.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(static segment =>
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
}
