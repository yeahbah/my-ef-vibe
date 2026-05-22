using CommandLine;
using Spectre.Console;

namespace MyEfVibe;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "scan", StringComparison.OrdinalIgnoreCase))
        {
            return Parser.Default.ParseArguments<ScanCliOptions>(args[1..])
                .MapResult(
                    (ScanCliOptions options) => ScanCommandRunner.RunFromOptionsAsync(options),
                    (IEnumerable<Error> errors) => Task.FromResult(CliParseHelper.PrintErrorsAndReturnFailure(errors)));
        }

        return Parser.Default.ParseArguments<EfvibeCliOptions>(args)
            .MapResult(
                (EfvibeCliOptions options) => RunEfvibeAsync(options),
                (IEnumerable<Error> errors) => Task.FromResult(CliParseHelper.PrintErrorsAndReturnFailure(errors)));
    }

    private static async Task<int> RunEfvibeAsync(EfvibeCliOptions options)
    {
        CliUi.Configure();

        return await InvokeAsync(
            CliPathHelper.ResolveWorkspace(options.Workspace),
            CliPathHelper.ToFileInfo(options.Project),
            CliPathHelper.ToFileInfo(options.StartupProject),
            options.Context,
            options.ConnectionString,
            options.Provider,
            options.Expression,
            options.DbLog,
            options.DbLogLevel,
            options.DbLogVerbose,
            options.AboutJson,
            options.Framework,
            options.ExpressionParts);
    }

    private static async Task<int> InvokeAsync(
        DirectoryInfo workspace,
        FileInfo? projectPath,
        FileInfo? startupProjectPath,
        string? contextFullName,
        string? connectionString,
        string? providerRaw,
        string? expressionOptionValue,
        bool dbLogEnabled,
        string? dbLogLevelRaw,
        bool dbLogVerbose,
        bool aboutJson,
        string? frameworkOrNull,
        IEnumerable<string>? expressionParts)
    {
        var dbLogSettings = new DbLogSettings { Enabled = dbLogEnabled, Verbose = dbLogVerbose };

        if (!string.IsNullOrWhiteSpace(dbLogLevelRaw)
            && DbLogLevelParser.TryParse(dbLogLevelRaw, out var parsedLevel))
        {
            dbLogSettings.Level = parsedLevel;
        }

        var parsedProvider = ProviderParser.ParseOrNull(providerRaw);

        if (!string.IsNullOrWhiteSpace(connectionString) && parsedProvider is null)
        {
            CliUi.WriteError("`--connection-string` requires `--provider` (sqlserver | npgsql | sqlite | oracle | mysql | mariadb).");
            return 3;
        }

        var oneShotExpression = CliPathHelper.ResolveOneShotExpression(expressionOptionValue, expressionParts);

        var workspaceRoot = SessionPaths.EnsureSessionDirectory(workspace.FullName);
        var searchDirectory = ProjectPathResolver.ResolveSearchDirectory(
            workspaceRoot,
            projectPath?.FullName,
            startupProjectPath?.FullName);

        FileInfo resolvedProject;
        FileInfo resolvedStartup;
        WorkspaceBuildResult workspaceBuild;

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
        catch (WorkspaceException workspaceFailure)
        {
            CliUi.WriteErrorPanel("Workspace failure", workspaceFailure.Message);
            return 10;
        }
        catch (InvalidOperationException selectionFailure)
        {
            CliUi.WriteErrorPanel("Workspace failure", selectionFailure.Message);
            return 10;
        }

        var projectLabel = Path.GetRelativePath(searchDirectory, resolvedProject.FullName);
        var startupLabel = Path.GetRelativePath(searchDirectory, resolvedStartup.FullName);

        AnsiConsole.MarkupLine($"[dim]Workspace root:[/] [cyan]{Markup.Escape(workspaceRoot)}[/]");
        AnsiConsole.MarkupLine($"[dim]EF project:[/] [cyan]{Markup.Escape(projectLabel)}[/]");

        if (!string.Equals(resolvedStartup.FullName, resolvedProject.FullName, StringComparison.OrdinalIgnoreCase))
            AnsiConsole.MarkupLine($"[dim]Startup project (config):[/] [cyan]{Markup.Escape(startupLabel)}[/]");

        var pendingSessionDirectory = SessionPaths.EnsurePendingSessionDirectory(workspaceRoot);

        try
        {
            workspaceBuild = CliUi.RunWithStatus(
                "Building EF project…",
                () => WorkspaceBuilder.BuildResolvedProject(
                    pendingSessionDirectory,
                    resolvedProject,
                    resolvedStartup,
                    frameworkOrNull));
        }
        catch (WorkspaceException workspaceFailure)
        {
            CliUi.WriteErrorPanel("Workspace failure", workspaceFailure.Message);
            return 10;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Built [cyan]{Markup.Escape(projectLabel)}[/]");

        using var host = WorkspaceHost.Load(workspaceBuild);

        Type dbContextType;

        try
        {
            dbContextType = DbContextActivator.ResolveContextType(
                host,
                contextFullName,
                allowInteractiveSelection: string.IsNullOrWhiteSpace(oneShotExpression));
        }
        catch (InvalidOperationException resolutionFailure)
        {
            CliUi.WriteErrorPanel("DbContext resolution failed", resolutionFailure.Message);
            return 14;
        }

        var sessionDirectory = SessionPaths.EnsureDbContextSessionDirectory(
            workspaceRoot,
            resolvedProject.FullName,
            dbContextType.Name);
        host.SetSessionDirectory(sessionDirectory);

        AnsiConsole.MarkupLine($"[dim]DbContext:[/] [cyan]{Markup.Escape(dbContextType.Name)}[/]");
        AnsiConsole.MarkupLine($"[dim]Session directory:[/] [cyan]{Markup.Escape(sessionDirectory)}[/]");

        object dbContextInstance;

        try
        {
            dbContextInstance = DbContextActivator.ResolveInstance(
                host,
                contextFullName,
                connectionString,
                parsedProvider,
                allowInteractiveSelection: string.IsNullOrWhiteSpace(oneShotExpression));
        }
        catch (InvalidOperationException resolutionFailure)
        {
            CliUi.WriteErrorPanel("DbContext resolution failed", resolutionFailure.Message);
            return 14;
        }

        var session = new ScriptSession(
            dbContextInstance.GetType(),
            dbContextInstance,
            workspaceBuild.ReferenceAssemblyPaths,
            host.AssemblyLoader);

        if (aboutJson)
        {
            AboutJsonReporter.Write(dbContextInstance, host, workspaceRoot, parsedProvider);
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(oneShotExpression))
            return await QueryRunner.RunOnceAsync(dbContextInstance, session, host, dbLogSettings, oneShotExpression);

        var repl = new QueryRepl(session, host, dbContextInstance, dbLogSettings, projectLabel);

        await repl.RunAsync();

        return 0;
    }
}
