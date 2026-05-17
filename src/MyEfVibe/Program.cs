using System.CommandLine;
using System.Diagnostics;
using Spectre.Console;

namespace MyEfVibe;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        CliUi.Configure();

        var workspaceOption = new Option<DirectoryInfo>(
            aliases: new[] { "-w", "--workspace" },
            description: "Session directory for exports and other artifacts (created if missing).")
        { IsRequired = true };

        var projectOption = new Option<FileInfo?>(
            aliases: new[] { "-p", "--project" },
            description: "EF Core project to build (`.csproj` with the DbContext).");

        var startupProjectOption = new Option<FileInfo?>(
            aliases: new[] { "-s", "--startup-project" },
            description: "Startup project for configuration (user secrets, appsettings). Auto-inferred from project references when omitted.");

        var contextOption = new Option<string?>(
            aliases: new[] { "-c", "--context" },
            description: "Fully qualified DbContext type name when multiple contexts are present.");

        var connectionOption = new Option<string?>(
            aliases: new[] { "--connection-string", "-cs" },
            description: "Connection string used when constructing `DbContextOptions<TContext>` manually.");

        var providerOption = new Option<string?>(
            aliases: new[] { "--provider" },
            description: "Database provider key used with `--connection-string`: sqlserver | npgsql | sqlite.");

        var expressionOption = new Option<string?>(
            aliases: new[] { "-e", "--expression" },
            description: "Run a single expression and exit (non-interactive).");

        var sqlOption = new Option<bool>(
            aliases: new[] { "--sql" },
            description: "Show generated SQL (executed commands and translated IQueryable SQL).",
            getDefaultValue: () => true);

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

        return await InvokeAsync(
            parseResult.GetValueForOption(workspaceOption)!,
            parseResult.GetValueForOption(projectOption),
            parseResult.GetValueForOption(startupProjectOption),
            parseResult.GetValueForOption(contextOption),
            parseResult.GetValueForOption(connectionOption),
            parseResult.GetValueForOption(providerOption),
            parseResult.GetValueForOption(expressionOption),
            parseResult.GetValueForOption(sqlOption),
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
        string[]? expressionArgumentTokens)
    {
        var sqlSettings = new SqlDisplaySettings { ShowSql = showSql };

        var parsedProvider = ProviderParser.ParseOrNull(providerRaw);

        if (!string.IsNullOrWhiteSpace(connectionString) && parsedProvider is null)
        {
            CliUi.WriteError("`--connection-string` requires `--provider` (sqlserver | npgsql | sqlite).");
            return 3;
        }

        var oneShotExpression = ResolveOneShotExpression(expressionOptionValue, expressionArgumentTokens);

        var sessionDirectory = SessionPaths.EnsureSessionDirectory(workspace.FullName);
        var searchDirectory = ProjectPathResolver.ResolveSearchDirectory(
            sessionDirectory,
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

        AnsiConsole.MarkupLine($"[dim]Session directory:[/] [cyan]{Markup.Escape(sessionDirectory)}[/]");
        AnsiConsole.MarkupLine($"[dim]EF project:[/] [cyan]{Markup.Escape(projectLabel)}[/]");

        if (!string.Equals(resolvedStartup.FullName, resolvedProject.FullName, StringComparison.OrdinalIgnoreCase))
            AnsiConsole.MarkupLine($"[dim]Startup project (config):[/] [cyan]{Markup.Escape(startupLabel)}[/]");

        try
        {
            workspaceBuild = CliUi.RunWithStatus(
                "Building EF project…",
                () => WorkspaceBuilder.BuildResolvedProject(sessionDirectory, resolvedProject, resolvedStartup));
        }
        catch (WorkspaceException workspaceFailure)
        {
            CliUi.WriteErrorPanel("Workspace failure", workspaceFailure.Message);
            return 10;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Built [cyan]{Markup.Escape(projectLabel)}[/]");

        using var host = WorkspaceHost.Load(workspaceBuild);

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
}
