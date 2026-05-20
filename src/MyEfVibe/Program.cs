using System.CommandLine;
using System.Diagnostics;
using Spectre.Console;

namespace MyEfVibe;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (IsVersionRequest(args))
        {
            Console.WriteLine(ToolInfo.GetVersion());
            return 0;
        }

        CliUi.Configure();

        var workspaceOption = new Option<DirectoryInfo?>(
            aliases: new[] { "-w", "--workspace" },
            description:
                "Session directory for exports and other artifacts (created if missing). "
                + "Default: ~/.efvibe on macOS/Linux, %APPDATA%/efvibe on Windows.");

        var projectOption = new Option<FileInfo?>(
            aliases: new[] { "-p", "--project" },
            description: "EF Core project to build (`.csproj` with the DbContext).");

        var startupProjectOption = new Option<FileInfo?>(
            aliases: new[] { "-s", "--startup-project" },
            description: "Startup project for configuration (user secrets, appsettings). Auto-inferred from project references when omitted.");

        var contextOption = new Option<string?>(
            aliases: new[] { "-c", "--context" },
            description: "DbContext type name (e.g. MyDbContext) or fully qualified name when multiple contexts are present.");

        var connectionOption = new Option<string?>(
            aliases: new[] { "--connection-string", "-cs" },
            description: "Connection string used when constructing `DbContextOptions<TContext>` manually.");

        var providerOption = new Option<string?>(
            aliases: new[] { "--provider" },
            description: "Database provider key used with `--connection-string`: sqlserver | npgsql | sqlite | oracle | mysql.");

        var expressionOption = new Option<string?>(
            aliases: new[] { "-e", "--expression" },
            description: "Run a single expression and exit (non-interactive).");

        var sqlOption = new Option<bool>(
            aliases: new[] { "--sql" },
            description: "Show generated SQL (executed commands and translated IQueryable SQL).",
            getDefaultValue: () => true);

        var versionOption = new Option<bool>(
            aliases: new[] { "--version", "-V" },
            description: "Show tool version and exit.");

        var aboutJsonOption = new Option<bool>(
            aliases: new[] { "--about-json" },
            description: "Write session metadata as JSON to stdout and exit (no REPL).");

        var frameworkOption = new Option<string?>(
            aliases: new[] { "-f", "--framework" },
            description:
                "Target framework moniker for building the workspace project (for example net8.0). "
                + "Defaults to a framework listed in the project file, not the efvibe tool runtime.");

        var expressionArgument = new Argument<string[]>("expression")

        {
            Arity = ArgumentArity.ZeroOrMore,
            Description = "Optional one-shot expression when `-e` is omitted but arguments are provided.",
        };

        var rootCommand = new RootCommand(
            "Interactive EF Core LINQ shell against another project's DbContext.")
        {
            workspaceOption,
            projectOption,
            startupProjectOption,
            contextOption,
            connectionOption,
            providerOption,
            expressionOption,
            sqlOption,
            versionOption,
            aboutJsonOption,
            frameworkOption,
            expressionArgument,
        };

        rootCommand.Name = "efvibe";

        var parseResult = rootCommand.Parse(args);

        if (parseResult.Errors.Count > 0)
        {
            foreach (var error in parseResult.Errors)
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(error.Message)}[/]");

            return 1;
        }

        if (parseResult.GetValueForOption(versionOption))
        {
            Console.WriteLine(ToolInfo.GetVersion());
            return 0;
        }

        var workspace = parseResult.GetValueForOption(workspaceOption)
            ?? new DirectoryInfo(SessionPaths.GetDefaultWorkspaceDirectory());

        return await InvokeAsync(
            workspace,
            parseResult.GetValueForOption(projectOption),
            parseResult.GetValueForOption(startupProjectOption),
            parseResult.GetValueForOption(contextOption),
            parseResult.GetValueForOption(connectionOption),
            parseResult.GetValueForOption(providerOption),
            parseResult.GetValueForOption(expressionOption),
            parseResult.GetValueForOption(sqlOption),
            parseResult.GetValueForOption(aboutJsonOption),
            parseResult.GetValueForOption(frameworkOption),
            parseResult.GetValueForArgument(expressionArgument));
    }

    private static async Task<int> InvokeAsync(
        DirectoryInfo workspace,
        FileInfo? projectPath,
        FileInfo? startupProjectPath,
        string? contextFullName,
        string? connectionString,
        string? providerRaw,
        string? expressionOptionValue,
        bool showSql,
        bool aboutJson,
        string? frameworkOrNull,
        string[]? expressionArgumentTokens)
    {
        var sqlSettings = new SqlDisplaySettings { ShowSql = showSql };

        var parsedProvider = ProviderParser.ParseOrNull(providerRaw);

        if (!string.IsNullOrWhiteSpace(connectionString) && parsedProvider is null)
        {
            CliUi.WriteError("`--connection-string` requires `--provider` (sqlserver | npgsql | sqlite | oracle | mysql).");
            return 3;
        }

        var oneShotExpression = ResolveOneShotExpression(expressionOptionValue, expressionArgumentTokens);

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
            return await QueryRunner.RunOnceAsync(dbContextInstance, session, host, sqlSettings, oneShotExpression);

        var repl = new QueryRepl(session, host, dbContextInstance, sqlSettings, projectLabel);

        await repl.RunAsync();

        return 0;
    }

    private static string? ResolveOneShotExpression(string? expressionOptionValue, string[]? expressionArgumentTokens)
    {
        if (!string.IsNullOrWhiteSpace(expressionOptionValue))
            return expressionOptionValue.Trim();

        if (expressionArgumentTokens is not { Length: > 0 })
            return null;

        return string.Join(' ', expressionArgumentTokens).Trim();
    }

    private static bool IsVersionRequest(string[] args)
    {
        if (args.Length is 0 or > 2)
            return false;

        foreach (var arg in args)
        {
            if (string.Equals(arg, "--version", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-V", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-v", StringComparison.OrdinalIgnoreCase))
                continue;

            return false;
        }

        return args.Length > 0;
    }
}
