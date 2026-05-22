namespace MyEfVibe;

internal static class WorkspaceRuntimeBootstrap
{
    internal static async Task<(WorkspaceRuntime? Runtime, int ExitCode, string? Error)> LoadAsync(
        DirectoryInfo workspace,
        FileInfo? projectPath,
        FileInfo? startupProjectPath,
        string? contextFullName,
        string? connectionString,
        string? providerRaw,
        bool dbLogEnabled,
        bool noDbLog,
        string? dbLogLevelRaw,
        bool dbLogVerbose,
        string? frameworkOrNull,
        CancellationToken cancellationToken = default)
    {
        var dbLogSettings = new DbLogSettings
        {
            Enabled = noDbLog ? false : dbLogEnabled,
            Verbose = dbLogVerbose,
        };

        if (!string.IsNullOrWhiteSpace(dbLogLevelRaw)
            && DbLogLevelParser.TryParse(dbLogLevelRaw, out var parsedLevel))
        {
            dbLogSettings.Level = parsedLevel;
        }

        var parsedProvider = ProviderParser.ParseOrNull(providerRaw);

        if (!string.IsNullOrWhiteSpace(connectionString) && parsedProvider is null)
            return (null, 3, "`--connection-string` requires `--provider`.");

        var workspaceRoot = SessionPaths.EnsureSessionDirectory(workspace.FullName);
        var searchDirectory = ProjectPathResolver.ResolveSearchDirectory(
            workspaceRoot,
            projectPath?.FullName,
            startupProjectPath?.FullName);

        FileInfo resolvedProject;
        FileInfo resolvedStartup;

        try
        {
            resolvedProject = WorkspaceProjectLocator.ResolveProject(
                searchDirectory,
                projectPath?.FullName);

            resolvedStartup = StartupProjectResolver.Resolve(
                searchDirectory,
                resolvedProject,
                startupProjectPath?.FullName);
        }
        catch (Exception failure)
        {
            return (null, 10, failure.Message);
        }

        var pendingSessionDirectory = SessionPaths.EnsurePendingSessionDirectory(workspaceRoot);

        WorkspaceBuildResult workspaceBuild;

        try
        {
            workspaceBuild = await Task.Run(
                () => WorkspaceBuilder.BuildResolvedProject(
                    pendingSessionDirectory,
                    resolvedProject,
                    resolvedStartup,
                    frameworkOrNull),
                cancellationToken);
        }
        catch (WorkspaceException workspaceFailure)
        {
            return (null, 10, workspaceFailure.Message);
        }

        var host = WorkspaceHost.Load(workspaceBuild);

        Type dbContextType;

        try
        {
            dbContextType = DbContextActivator.ResolveContextType(
                host,
                contextFullName,
                allowInteractiveSelection: false);
        }
        catch (InvalidOperationException resolutionFailure)
        {
            host.Dispose();
            return (null, 14, resolutionFailure.Message);
        }

        var sessionDirectory = SessionPaths.EnsureDbContextSessionDirectory(
            workspaceRoot,
            resolvedProject.FullName,
            dbContextType.Name);
        host.SetSessionDirectory(sessionDirectory);

        object dbContextInstance;

        try
        {
            dbContextInstance = DbContextActivator.ResolveInstance(
                host,
                contextFullName,
                connectionString,
                parsedProvider,
                allowInteractiveSelection: false);
        }
        catch (InvalidOperationException resolutionFailure)
        {
            host.Dispose();
            return (null, 14, resolutionFailure.Message);
        }

        var session = new ScriptSession(
            dbContextInstance.GetType(),
            dbContextInstance,
            workspaceBuild.ReferenceAssemblyPaths,
            host.AssemblyLoader);

        return (
            new WorkspaceRuntime(
                host,
                session,
                dbContextInstance,
                dbLogSettings,
                workspaceRoot,
                sessionDirectory,
                dbContextType.Name),
            0,
            null);
    }
}
