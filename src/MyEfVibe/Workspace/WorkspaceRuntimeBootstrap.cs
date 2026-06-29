namespace MyEfVibe.Workspace;

internal static class WorkspaceRuntimeBootstrap
{
    internal static async Task<(WorkspaceRuntime? Runtime, int ExitCode, string? Error)> LoadAsync(
        DirectoryInfo workspace,
        FileInfo? projectPath,
        FileInfo? startupProjectPath,
        string? contextFullName,
        string? connectionString,
        bool dbLogEnabled,
        bool noDbLog,
        string? dbLogLevelRaw,
        bool dbLogVerbose,
        string? frameworkOrNull,
        WorkspaceBuildPolicy buildPolicy = WorkspaceBuildPolicy.Auto,
        ScriptSessionConfiguration? scriptConfiguration = null,
        CancellationToken cancellationToken = default)
    {
        var dbLogSettings = new DbLogSettings
        {
            Enabled = noDbLog ? false : dbLogEnabled,
            Verbose = dbLogVerbose
        };

        if (!string.IsNullOrWhiteSpace(dbLogLevelRaw)
            && DbLogLevelParser.TryParse(dbLogLevelRaw, out var parsedLevel))
        {
            dbLogSettings.Level = parsedLevel;
        }

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
                    frameworkOrNull,
                    buildPolicy).Result,
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
                false);
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
                null,
                false);
        }
        catch (InvalidOperationException resolutionFailure)
        {
            host.Dispose();
            return (null, 14, resolutionFailure.Message);
        }

        var effectiveScriptConfiguration = scriptConfiguration ?? ScriptSessionConfiguration.Empty;
        var scriptBootstrapBase = effectiveScriptConfiguration.ResolveBasePath(searchDirectory);

        var session = new ScriptSession(
            dbContextInstance.GetType(),
            dbContextInstance,
            workspaceBuild.ReferenceAssemblyPaths,
            host.AssemblyLoader,
            ProviderCapabilityResolver.RequiresAsyncQueries(host.ActiveProviderDescriptor),
            effectiveScriptConfiguration,
            scriptBootstrapBase);

        try
        {
            await session.InitializeAsync(scriptBootstrapBase, cancellationToken);
        }
        catch (Exception bootstrapFailure)
        {
            host.Dispose();
            return (null, 10, bootstrapFailure.Message);
        }

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