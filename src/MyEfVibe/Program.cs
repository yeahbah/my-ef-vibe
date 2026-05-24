using CommandLine;
using Spectre.Console;

namespace MyEfVibe;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "scan", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length > 1 && string.Equals(args[1], "note", StringComparison.OrdinalIgnoreCase))
            {
                return Parser.Default.ParseArguments<ScanNoteCliOptions>(args[2..])
                    .MapResult(
                        (ScanNoteCliOptions options) => ScanNoteCommandRunner.RunFromOptionsAsync(options),
                        (IEnumerable<Error> errors) => Task.FromResult(CliParseHelper.PrintErrorsAndReturnFailure(errors)));
            }

            if (args.Length > 1 && string.Equals(args[1], "dismiss", StringComparison.OrdinalIgnoreCase))
            {
                return Parser.Default.ParseArguments<ScanDismissCliOptions>(args[2..])
                    .MapResult(
                        (ScanDismissCliOptions options) => ScanDismissCommandRunner.RunFromOptionsAsync(options),
                        (IEnumerable<Error> errors) => Task.FromResult(CliParseHelper.PrintErrorsAndReturnFailure(errors)));
            }

            return Parser.Default.ParseArguments<ScanCliOptions>(args[1..])
                .MapResult(
                    (ScanCliOptions options) => ScanCommandRunner.RunFromOptionsAsync(options),
                    (IEnumerable<Error> errors) => Task.FromResult(CliParseHelper.PrintErrorsAndReturnFailure(errors)));
        }

        if (args.Length > 0 && string.Equals(args[0], "serve", StringComparison.OrdinalIgnoreCase))
        {
            return Parser.Default.ParseArguments<ServeCliOptions>(args[1..])
                .MapResult(
                    (ServeCliOptions options) => ServeCommandRunner.RunFromOptionsAsync(options),
                    (IEnumerable<Error> errors) => Task.FromResult(CliParseHelper.PrintErrorsAndReturnFailure(errors)));
        }

        if (args.Length > 0 && string.Equals(args[0], "language-server", StringComparison.OrdinalIgnoreCase))
        {
            return Parser.Default.ParseArguments<LanguageServerCliOptions>(args[1..])
                .MapResult(
                    (LanguageServerCliOptions options) => LanguageServerCommandRunner.RunFromOptionsAsync(options),
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
            options.NoDbLog,
            options.DbLogLevel,
            options.DbLogVerbose,
            options.AboutJson,
            options.TablesJson,
            options.DescribeJson,
            options.DbInfoJson,
            options.CompletionsJson,
            options.Format,
            options.NoBanner,
            options.WithPlan,
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
        bool noDbLog,
        string? dbLogLevelRaw,
        bool dbLogVerbose,
        bool aboutJson,
        bool tablesJson,
        string? describeJsonEntity,
        bool dbInfoJson,
        string? completionsPrefix,
        string? formatRaw,
        bool noBanner,
        bool withPlan,
        string? frameworkOrNull,
        IEnumerable<string>? expressionParts)
    {
        if (!TryParseOutputFormat(formatRaw, out var outputFormat, out var formatError))
        {
            CliUi.WriteError(formatError!);
            return 1;
        }

        var quietOutput = noBanner || outputFormat == CliOutputFormat.Json || aboutJson;

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

        if (!quietOutput)
        {
            AnsiConsole.MarkupLine($"[dim]Workspace root:[/] [cyan]{Markup.Escape(workspaceRoot)}[/]");
            AnsiConsole.MarkupLine($"[dim]EF project:[/] [cyan]{Markup.Escape(projectLabel)}[/]");

            if (!string.Equals(resolvedStartup.FullName, resolvedProject.FullName, StringComparison.OrdinalIgnoreCase))
                AnsiConsole.MarkupLine($"[dim]Startup project (config):[/] [cyan]{Markup.Escape(startupLabel)}[/]");
        }

        var pendingSessionDirectory = SessionPaths.EnsurePendingSessionDirectory(workspaceRoot);

        try
        {
            workspaceBuild = quietOutput
                ? WorkspaceBuilder.BuildResolvedProject(
                    pendingSessionDirectory,
                    resolvedProject,
                    resolvedStartup,
                    frameworkOrNull)
                : CliUi.RunWithStatus(
                    "Building EF project…",
                    () => WorkspaceBuilder.BuildResolvedProject(
                        pendingSessionDirectory,
                        resolvedProject,
                        resolvedStartup,
                        frameworkOrNull));
        }
        catch (WorkspaceException workspaceFailure)
        {
            if (quietOutput)
                Console.Error.WriteLine(workspaceFailure.Message);
            else
                CliUi.WriteErrorPanel("Workspace failure", workspaceFailure.Message);

            return 10;
        }

        if (!quietOutput)
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

        if (!quietOutput)
        {
            AnsiConsole.MarkupLine($"[dim]DbContext:[/] [cyan]{Markup.Escape(dbContextType.Name)}[/]");
            AnsiConsole.MarkupLine($"[dim]Session directory:[/] [cyan]{Markup.Escape(sessionDirectory)}[/]");
        }

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

        if (tablesJson)
        {
            TablesJsonReporter.Write(dbContextInstance);
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(describeJsonEntity))
        {
            DescribeJsonReporter.Write(dbContextInstance, describeJsonEntity);
            return 0;
        }

        if (dbInfoJson)
        {
            await DbInfoJsonReporter.WriteAsync(dbContextInstance, host);
            return 0;
        }

        if (completionsPrefix is not null)
        {
            CompletionsJsonReporter.Write(dbContextInstance, completionsPrefix);
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(oneShotExpression))
            return await QueryRunner.RunOnceAsync(
                dbContextInstance,
                session,
                host,
                dbLogSettings,
                oneShotExpression,
                outputFormat,
                withPlan);

        var repl = new QueryRepl(session, host, dbContextInstance, dbLogSettings, projectLabel);

        await repl.RunAsync();

        return 0;
    }

    private static bool TryParseOutputFormat(string? formatRaw, out CliOutputFormat format, out string? error)
    {
        if (string.IsNullOrWhiteSpace(formatRaw))
        {
            format = CliOutputFormat.Text;
            error = null;
            return true;
        }

        if (string.Equals(formatRaw, "json", StringComparison.OrdinalIgnoreCase))
        {
            format = CliOutputFormat.Json;
            error = null;
            return true;
        }

        if (string.Equals(formatRaw, "text", StringComparison.OrdinalIgnoreCase))
        {
            format = CliOutputFormat.Text;
            error = null;
            return true;
        }

        format = CliOutputFormat.Text;
        error = $"Unknown output format '{formatRaw}'. Use text or json.";
        return false;
    }
}
