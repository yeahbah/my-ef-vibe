namespace MyEfVibe.IntegrationTests;

internal sealed record IntegrationScenario(
    string Id,
    string Provider,
    string RepoRoot,
    string EfProjectRelativePath,
    string StartupProjectRelativePath,
    string Context,
    string Framework,
    string? ConnectionString)
{
    internal string EfProjectPath => Path.Combine(RepoRoot, EfProjectRelativePath);

    internal string StartupProjectPath => Path.Combine(RepoRoot, StartupProjectRelativePath);
}