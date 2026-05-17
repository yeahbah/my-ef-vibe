namespace MyEfVibe;

internal static class WorkspaceProjectSelector
{
    private const int AutoPickScoreGap = 15;

    internal sealed record RankedProject(string CsprojPath, int Score, string Label);

    internal static FileInfo Resolve(string workspaceDirectory, string? explicitCsprojPathOrNull)
    {
        var normalizedWorkspace =
            Path.GetFullPath(workspaceDirectory.TrimEnd(Path.DirectorySeparatorChar));

        if (!Directory.Exists(normalizedWorkspace))
            throw new WorkspaceException($"Workspace `{normalizedWorkspace}` does not exist.");

        if (!string.IsNullOrWhiteSpace(explicitCsprojPathOrNull))
            return LocateExplicit(explicitCsprojPathOrNull, normalizedWorkspace);

        var discovered = DiscoverCsproj(normalizedWorkspace)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (discovered.Length == 0)
            throw new WorkspaceException($"No `.csproj` files were discovered under `{normalizedWorkspace}`.");

        if (discovered.Length == 1)
            return new FileInfo(discovered.Single());

        var ranked = RankProjects(normalizedWorkspace, discovered);

        if (ranked.Count == 0 || ranked[0].Score <= 0)
            return PromptAmongAllProjects(normalizedWorkspace, discovered);

        if (ranked.Count == 1
            || ranked[0].Score - ranked[1].Score >= AutoPickScoreGap
            || !InteractiveSelection.CanPrompt)
            return new FileInfo(ranked[0].CsprojPath);

        return PromptAmongRankedProjects(ranked);
    }

    private static List<RankedProject> RankProjects(string workspaceDirectory, IReadOnlyList<string> csprojPaths)
    {
        var containsDbContext = csprojPaths.ToDictionary(
            static path => path,
            static path => DbContextSourceScanner.ProjectContainsDbContext(Path.GetDirectoryName(path)!),
            StringComparer.OrdinalIgnoreCase);

        return csprojPaths
            .Select(path => new RankedProject(
                path,
                ScoreProject(path, containsDbContext),
                BuildLabel(workspaceDirectory, path, containsDbContext)))
            .Where(static candidate => candidate.Score > 0)
            .OrderByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.CsprojPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int ScoreProject(string csprojPath, IReadOnlyDictionary<string, bool> containsDbContext)
    {
        if (CsprojInspector.IsTestProject(csprojPath))
            return -100;

        var score = 0;
        var projectDirectory = Path.GetDirectoryName(csprojPath)!;
        var fileName = Path.GetFileNameWithoutExtension(csprojPath);

        if (containsDbContext.GetValueOrDefault(csprojPath))
            score += 40;

        if (CsprojInspector.HasEfCorePackageReference(csprojPath))
            score += 20;

        if (CsprojInspector.IsExecutableOutput(csprojPath))
            score += 25;

        if (CsprojInspector.UsesWebSdk(csprojPath))
            score += 30;

        if (ReferencesDbContextProject(csprojPath, containsDbContext))
            score += 35;

        if (fileName.EndsWith(".API", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("Api", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("Host", StringComparison.OrdinalIgnoreCase))
            score += 10;

        if (projectDirectory.Contains($"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase)
            || projectDirectory.Contains($"{Path.DirectorySeparatorChar}database{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            score -= 25;

        return score;
    }

    private static string BuildLabel(
        string workspaceDirectory,
        string csprojPath,
        IReadOnlyDictionary<string, bool> containsDbContext)
    {
        var relative = Path.GetRelativePath(workspaceDirectory, csprojPath);
        var traits = new List<string>();

        if (containsDbContext.GetValueOrDefault(csprojPath))
            traits.Add("DbContext");

        if (CsprojInspector.IsExecutableOutput(csprojPath))
            traits.Add("executable");

        if (CsprojInspector.HasEfCorePackageReference(csprojPath))
            traits.Add("EF Core");

        if (ReferencesDbContextProject(csprojPath, containsDbContext))
            traits.Add("references DbContext project");

        return traits.Count == 0
            ? relative
            : $"{relative} [grey]({string.Join(", ", traits)})[/]";
    }

    private static bool ReferencesDbContextProject(
        string csprojPath,
        IReadOnlyDictionary<string, bool> containsDbContext,
        int maxDepth = 5)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((csprojPath, 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();

            if (depth > maxDepth)
                continue;

            foreach (var referencePath in CsprojInspector.GetProjectReferencePaths(current))
            {
                if (containsDbContext.GetValueOrDefault(referencePath))
                    return true;

                if (visited.Add(referencePath))
                    queue.Enqueue((referencePath, depth + 1));
            }
        }

        return false;
    }

    private static FileInfo PromptAmongRankedProjects(IReadOnlyList<RankedProject> ranked)
    {
        var choice = InteractiveSelection.Choose(
            "[bold]Multiple EF host projects found. Which project should be built?[/]",
            ranked.Select(candidate => new SelectionOption<string>(
                candidate.CsprojPath,
                $"{candidate.Label} [dim](score {candidate.Score})[/]")).ToArray());

        return new FileInfo(choice);
    }

    private static FileInfo PromptAmongAllProjects(string workspaceDirectory, IReadOnlyList<string> csprojPaths)
    {
        if (!InteractiveSelection.CanPrompt)
        {
            throw new WorkspaceException(
                "Could not infer an EF Core host project for this workspace."
                + $"{Environment.NewLine}Specify the startup project with `-p/--project path/to/Host.csproj`."
                + $"{Environment.NewLine}Discovered projects:"
                + $"{Environment.NewLine}{string.Join(Environment.NewLine, csprojPaths.Select(path => $" - {Path.GetRelativePath(workspaceDirectory, path)}"))}");
        }

        var ordered = csprojPaths.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray();

        var choice = InteractiveSelection.Choose(
            "[bold]Which project should be built?[/]",
            ordered.Select(path => new SelectionOption<string>(
                path,
                Path.GetRelativePath(workspaceDirectory, path))).ToArray());

        return new FileInfo(choice);
    }

    private static FileInfo LocateExplicit(string explicitCandidate, string normalizedWorkspace)
    {
        var candidatePath =
            Path.IsPathRooted(explicitCandidate)
                ? explicitCandidate
                : Path.GetFullPath(Path.Combine(normalizedWorkspace, explicitCandidate));

        candidatePath = candidatePath.TrimEnd(Path.DirectorySeparatorChar);

        if (!File.Exists(candidatePath))
            throw new WorkspaceException($"Specified project `{candidatePath}` does not exist.");

        if (!string.Equals(Path.GetExtension(candidatePath), ".csproj", StringComparison.OrdinalIgnoreCase))
            throw new WorkspaceException("`-p/--project` must resolve to a `.csproj` file.");

        return new FileInfo(candidatePath);
    }

    private static IEnumerable<string> DiscoverCsproj(string normalizedWorkspaceDirectory)
        =>
        Directory
            .EnumerateFiles(normalizedWorkspaceDirectory, "*.csproj", SearchOption.AllDirectories)
            .Where(static path => !IsUnderBuildArtifacts(path));

    private static bool IsUnderBuildArtifacts(string absolutePathCandidate)
        =>
        absolutePathCandidate.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(static segment =>
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
}
