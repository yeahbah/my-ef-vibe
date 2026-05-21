namespace MyEfVibe.IntegrationTests;

internal sealed class EfvibeIntegrationSession : IAsyncDisposable
{
    private EfvibeIntegrationSession(
        IntegrationScenario scenario,
        WorkspaceBuildResult workspaceBuild,
        WorkspaceHost host,
        object dbContext,
        ScriptSession scriptSession,
        string sessionDirectory)
    {
        Scenario = scenario;
        WorkspaceBuild = workspaceBuild;
        Host = host;
        DbContext = dbContext;
        ScriptSession = scriptSession;
        SessionDirectory = sessionDirectory;
    }

    internal IntegrationScenario Scenario { get; }

    internal WorkspaceBuildResult WorkspaceBuild { get; }

    internal WorkspaceHost Host { get; }

    internal object DbContext { get; }

    internal ScriptSession ScriptSession { get; }

    internal string SessionDirectory { get; }

    internal static async Task<EfvibeIntegrationSession> ConnectAsync(
        IntegrationScenario scenario,
        CancellationToken cancellationToken = default)
    {
        if (!DatabaseProbe.TryValidateScenario(scenario, out var validationFailure))
            throw new InvalidOperationException(validationFailure);

        var workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            "efvibe-integration",
            scenario.Id,
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(workspaceRoot);

        var efProject = new FileInfo(scenario.EfProjectPath);
        var startupProject = new FileInfo(scenario.StartupProjectPath);

        var workspaceBuild = WorkspaceBuilder.BuildResolvedProject(
            Path.Combine(workspaceRoot, "pending"),
            efProject,
            startupProject,
            scenario.Framework);

        var host = WorkspaceHost.Load(workspaceBuild);

        var dbContextType = DbContextActivator.ResolveContextType(
            host,
            scenario.Context,
            allowInteractiveSelection: false);

        var sessionDirectory = SessionPaths.EnsureDbContextSessionDirectory(
            workspaceRoot,
            efProject.FullName,
            dbContextType.Name);

        host.SetSessionDirectory(sessionDirectory);

        var provider = ProviderParser.ParseOrNull(scenario.Provider);

        var dbContext = DbContextActivator.ResolveInstance(
            host,
            scenario.Context,
            scenario.ConnectionString,
            provider,
            allowInteractiveSelection: false);

        var scriptSession = new ScriptSession(
            dbContext.GetType(),
            dbContext,
            workspaceBuild.ReferenceAssemblyPaths,
            host.AssemblyLoader);

        if (!await DatabaseProbe.CanConnectAsync(dbContext, scriptSession, host, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Database for scenario `{scenario.Id}` is not reachable. Start the provider container and verify connection settings.");
        }

        return new EfvibeIntegrationSession(
            scenario,
            workspaceBuild,
            host,
            dbContext,
            scriptSession,
            sessionDirectory);
    }

    public ValueTask DisposeAsync()
    {
        Host.Dispose();
        return ValueTask.CompletedTask;
    }
}
