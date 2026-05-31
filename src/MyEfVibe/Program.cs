using CommandLine;
using Spectre.Console;

namespace MyEfVibe;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        if (args.Length > 0)
        {
            if (string.Equals(args[0], "scan", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(args[1], "note", StringComparison.OrdinalIgnoreCase))
                {
                    return Parser.Default.ParseArguments<ScanNoteCliOptions>(args[2..])
                        .MapResult(
                            options => ScanNoteCommandRunner.RunFromOptionsAsync(options),
                            errors => Task.FromResult(CliParseHelper.PrintErrorsAndReturnFailure(errors)));
                }

                if (string.Equals(args[1], "dismiss", StringComparison.OrdinalIgnoreCase))
                {
                    return Parser.Default.ParseArguments<ScanDismissCliOptions>(args[2..])
                        .MapResult(
                            options => ScanDismissCommandRunner.RunFromOptionsAsync(options),
                            errors => Task.FromResult(CliParseHelper.PrintErrorsAndReturnFailure(errors)));
                }

                return Parser.Default.ParseArguments<ScanCliOptions>(args[1..])
                    .MapResult(
                        options => ScanCommandRunner.RunFromOptionsAsync(options),
                        errors => Task.FromResult(CliParseHelper.PrintErrorsAndReturnFailure(errors)));
            }

            if (string.Equals(args[0], "serve", StringComparison.OrdinalIgnoreCase))
            {
                return Parser.Default.ParseArguments<ServeCliOptions>(args[1..])
                    .MapResult(
                        options => ServeCommandRunner.RunFromOptionsAsync(options),
                        errors => Task.FromResult(CliParseHelper.PrintErrorsAndReturnFailure(errors)));
            }

            if (string.Equals(args[0], "language-server", StringComparison.OrdinalIgnoreCase))
            {
                return Parser.Default.ParseArguments<LanguageServerCliOptions>(args[1..])
                    .MapResult(
                        options => LanguageServerCommandRunner.RunFromOptionsAsync(options),
                        errors => Task.FromResult(CliParseHelper.PrintErrorsAndReturnFailure(errors)));
            }
        }


        return Parser.Default.ParseArguments<EfvibeCliOptions>(args)
            .MapResult(
                options => RunEfvibeAsync(options),
                errors => Task.FromResult(CliParseHelper.PrintErrorsAndReturnFailure(errors)));
    }

    private static async Task<int> RunEfvibeAsync(EfvibeCliOptions options)
    {
        CliUi.Configure();
        return await InvokeAsync(options);
    }

    private static async Task<int> InvokeAsync(EfvibeCliOptions options)
    {
        if (options.AboutJson)
        {
            AboutJsonReporter.Write();
            return 0;
        }

        var parseOutputResult = TryParseOutputFormat(options.Format);
        if (parseOutputResult.ErrorMessage is not null)
        {
            CliUi.WriteError(parseOutputResult.ErrorMessage);
            return 1;
        }

        var quietOutput = options.NoBanner || parseOutputResult.Format == CliOutputFormat.Json;
        var dbLogSettings = new DbLogSettings
        {
            Enabled = options is { NoDbLog: false, DbLog: true },
            Verbose = options.DbLogVerbose
        };

        if (!string.IsNullOrWhiteSpace(options.DbLogLevel)
            && DbLogLevelParser.TryParse(options.DbLogLevel, out var parsedLevel))
        {
            dbLogSettings.Level = parsedLevel;
        }

        var parsedProvider = ProviderParser.ParseOrNull(options.Provider);
        if (!string.IsNullOrWhiteSpace(options.ConnectionString) && parsedProvider is null)
        {
            CliUi.WriteError(
                "`--connection-string` requires `--provider` (sqlserver | npgsql | sqlite | oracle | mysql | mariadb).");
            return 3;
        }

        var oneShotExpression = CliPathHelper.ResolveOneShotExpression(options.Expression, options.ExpressionParts);
        var workspace = CliPathHelper.ResolveWorkspace(options.Workspace);
        var workspaceRoot = SessionPaths.EnsureSessionDirectory(workspace.FullName);
        var project = CliPathHelper.ToFileInfo(options.Project);
        var startup = CliPathHelper.ToFileInfo(options.StartupProject);
        var searchDirectory = ProjectPathResolver.ResolveSearchDirectory(
            workspaceRoot,
            project?.FullName,
            startup?.FullName);

        FileInfo resolvedProject;
        FileInfo resolvedStartup;
        WorkspaceBuildResult workspaceBuild;
        try
        {
            resolvedProject = WorkspaceProjectLocator.ResolveProject(
                searchDirectory,
                project?.FullName);

            resolvedStartup = StartupProjectResolver.Resolve(
                searchDirectory,
                resolvedProject,
                startup?.FullName);
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
            {
                AnsiConsole.MarkupLine($"[dim]Startup project (config):[/] [cyan]{Markup.Escape(startupLabel)}[/]");
            }
        }

        var pendingSessionDirectory = SessionPaths.EnsurePendingSessionDirectory(workspaceRoot);
        try
        {
            workspaceBuild = quietOutput
                ? WorkspaceBuilder.BuildResolvedProject(
                    pendingSessionDirectory,
                    resolvedProject,
                    resolvedStartup,
                    options.Framework)
                : CliUi.RunWithStatus(
                    "Building EF project…",
                    () => WorkspaceBuilder.BuildResolvedProject(
                        pendingSessionDirectory,
                        resolvedProject,
                        resolvedStartup,
                        options.Framework));
        }
        catch (WorkspaceException workspaceFailure)
        {
            if (quietOutput)
            {
                await Console.Error.WriteLineAsync(workspaceFailure.Message);
            }
            else
            {
                CliUi.WriteErrorPanel("Workspace failure", workspaceFailure.Message);
            }

            return 10;
        }

        if (!quietOutput)
        {
            AnsiConsole.MarkupLine($"[green]✓[/] Built [cyan]{Markup.Escape(projectLabel)}[/]");
        }

        using var host = WorkspaceHost.Load(workspaceBuild);

        var headlessJsonOutput = options.TablesJson
                                 || options.DbInfoJson
                                 || options.CompletionsJson is not null
                                 || !string.IsNullOrWhiteSpace(options.DescribeJson);

        var allowInteractiveSelection = string.IsNullOrWhiteSpace(oneShotExpression) && !headlessJsonOutput;

        Type dbContextType;
        try
        {
            dbContextType = DbContextActivator.ResolveContextType(
                host,
                options.Context,
                allowInteractiveSelection);
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
                options.Context,
                options.ConnectionString,
                parsedProvider,
                allowInteractiveSelection);
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

        if (options.TablesJson)
        {
            TablesJsonReporter.Write(dbContextInstance);
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(options.DescribeJson))
        {
            DescribeJsonReporter.Write(dbContextInstance, options.DescribeJson);
            return 0;
        }

        if (options.DbInfoJson)
        {
            await DbInfoJsonReporter.WriteAsync(dbContextInstance, host);
            return 0;
        }

        if (options.CompletionsJson is not null)
        {
            CompletionsJsonReporter.Write(dbContextInstance, options.CompletionsJson);
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(oneShotExpression))
        {
            return await QueryRunner.RunOnceAsync(
                dbContextInstance,
                session,
                host,
                dbLogSettings,
                oneShotExpression,
                parseOutputResult.Format,
                options.WithPlan);
        }

        var repl = new QueryRepl(session, host, dbContextInstance, dbLogSettings, projectLabel);

        await repl.RunAsync();

        return 0;
    }

    private static (CliOutputFormat Format, string? ErrorMessage) TryParseOutputFormat(string? formatRaw)
    {
        if (string.IsNullOrWhiteSpace(formatRaw))
        {
            return (CliOutputFormat.Text, null);
        }

        if (string.Equals(formatRaw, "json", StringComparison.OrdinalIgnoreCase))
        {
            return (CliOutputFormat.Json, null);
        }
        
        if (string.Equals(formatRaw, "text", StringComparison.OrdinalIgnoreCase))
        {
            return (CliOutputFormat.Text, null);
        }
        
        return (CliOutputFormat.Text, $"Unknown output format '{formatRaw}'. Use text or json.");
    }
}