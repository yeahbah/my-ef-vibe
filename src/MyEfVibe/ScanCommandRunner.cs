using System.CommandLine;
using Spectre.Console;

namespace MyEfVibe;

internal static class ScanCommandRunner
{
    private sealed record ScanCommandDefinition(
        Command Command,
        Argument<string> ModeArgument,
        Option<DirectoryInfo?> WorkspaceOption,
        Option<FileInfo?> ProjectOption,
        Option<FileInfo?> StartupProjectOption,
        Option<string?> ContextOption,
        Option<string?> FrameworkOption,
        Option<string?> FailOnOption,
        Option<string?> MinSeverityOption,
        Option<bool> RespectDismissalsOption,
        Option<bool> JsonOption,
        Option<string?> ConnectionOption,
        Option<string?> ProviderOption);

    private static ScanCommandDefinition CreateDefinition()
    {
        var modeArgument = new Argument<string>("mode")
        {
            Description = "Scan mode: lite (static heuristics) or deep (heuristics + SQL translation).",
        };

        var workspaceOption = new Option<DirectoryInfo?>(
            aliases: new[] { "-w", "--workspace" },
            description: "Workspace root for scan artifacts.");

        var projectOption = new Option<FileInfo?>(
            aliases: new[] { "-p", "--project" },
            description: "EF Core project (.csproj) to scan.");

        var startupProjectOption = new Option<FileInfo?>(
            aliases: new[] { "-s", "--startup-project" },
            description: "Startup project for configuration (optional).");

        var contextOption = new Option<string?>(
            aliases: new[] { "-c", "--context" },
            description: "DbContext type name (required for deep scan when multiple contexts exist).");

        var frameworkOption = new Option<string?>(
            aliases: new[] { "-f", "--framework" },
            description: "Target framework moniker for building the project (e.g. net10.0).");

        var failOnOption = new Option<string?>(
            aliases: new[] { "--fail-on" },
            description: "Exit code 1 when any finding has this severity or higher (info | warning | error | critical).");

        var minSeverityOption = new Option<string?>(
            aliases: new[] { "--min-severity" },
            description: "Only report findings at or above this severity (info | warning | error | critical).");

        var respectDismissalsOption = new Option<bool>(
            aliases: new[] { "--respect-dismissals" },
            description: "Exclude findings previously dismissed in the REPL session.",
            getDefaultValue: () => false);

        var jsonOption = new Option<bool>(
            aliases: new[] { "--json" },
            description: "Write scan summary and findings as JSON to stdout.",
            getDefaultValue: () => false);

        var connectionOption = new Option<string?>(
            aliases: new[] { "--connection-string", "-cs" },
            description: "Connection string for deep scan (requires --provider).");

        var providerOption = new Option<string?>(
            aliases: new[] { "--provider" },
            description: "Database provider for deep scan with --connection-string.");

        var command = new Command("scan", "Run LINQ scan for CI (lite or deep) and optionally fail on severity.")
        {
            modeArgument,
            workspaceOption,
            projectOption,
            startupProjectOption,
            contextOption,
            frameworkOption,
            failOnOption,
            minSeverityOption,
            respectDismissalsOption,
            jsonOption,
            connectionOption,
            providerOption,
        };

        return new ScanCommandDefinition(
            command,
            modeArgument,
            workspaceOption,
            projectOption,
            startupProjectOption,
            contextOption,
            frameworkOption,
            failOnOption,
            minSeverityOption,
            respectDismissalsOption,
            jsonOption,
            connectionOption,
            providerOption);
    }

    internal static async Task<int> RunFromArgsAsync(string[] scanArgs, CancellationToken cancellationToken = default)
    {
        CliUi.Configure();

        var definition = CreateDefinition();
        var parseResult = definition.Command.Parse(scanArgs);

        if (parseResult.Errors.Count > 0)
        {
            foreach (var error in parseResult.Errors)
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(error.Message)}[/]");

            return 1;
        }

        return await RunAsync(
            parseResult.GetValueForArgument(definition.ModeArgument),
            parseResult.GetValueForOption(definition.WorkspaceOption)
                ?? new DirectoryInfo(SessionPaths.GetDefaultWorkspaceDirectory()),
            parseResult.GetValueForOption(definition.ProjectOption),
            parseResult.GetValueForOption(definition.StartupProjectOption),
            parseResult.GetValueForOption(definition.ContextOption),
            parseResult.GetValueForOption(definition.FrameworkOption),
            parseResult.GetValueForOption(definition.FailOnOption),
            parseResult.GetValueForOption(definition.MinSeverityOption),
            parseResult.GetValueForOption(definition.RespectDismissalsOption),
            parseResult.GetValueForOption(definition.JsonOption),
            parseResult.GetValueForOption(definition.ConnectionOption),
            parseResult.GetValueForOption(definition.ProviderOption),
            cancellationToken);
    }

    internal static async Task<int> RunAsync(
        string? modeRaw,
        DirectoryInfo workspace,
        FileInfo? projectPath,
        FileInfo? startupProjectPath,
        string? contextFullName,
        string? frameworkOrNull,
        string? failOnRaw,
        string? minSeverityRaw,
        bool respectDismissals,
        bool jsonOutput,
        string? connectionString,
        string? providerRaw,
        CancellationToken cancellationToken = default)
    {
        CliUi.Configure();

        if (!TryParseMode(modeRaw, out var mode))
        {
            CliUi.WriteError("Usage: efvibe scan <lite|deep> [options]");
            return 2;
        }

        if (!TryParseOptionalSeverity(failOnRaw, out var failOn, out var failOnError))
        {
            CliUi.WriteError(failOnError!);
            return 2;
        }

        if (!TryParseOptionalSeverity(minSeverityRaw, out var minSeverity, out var minSeverityError))
        {
            CliUi.WriteError(minSeverityError!);
            return 2;
        }

        var parsedProvider = ProviderParser.ParseOrNull(providerRaw);

        if (!string.IsNullOrWhiteSpace(connectionString) && parsedProvider is null)
        {
            CliUi.WriteError("`--connection-string` requires `--provider` (sqlserver | npgsql | sqlite | oracle | mysql | mariadb).");
            return 3;
        }

        var workspaceRoot = SessionPaths.EnsureSessionDirectory(workspace.FullName);

        FileInfo resolvedProject;
        FileInfo resolvedStartup;

        try
        {
            var searchDirectory = ProjectPathResolver.ResolveSearchDirectory(
                workspaceRoot,
                projectPath?.FullName,
                startupProjectPath?.FullName);

            resolvedProject = WorkspaceProjectLocator.ResolveProject(
                searchDirectory,
                projectPath?.FullName);

            resolvedStartup = StartupProjectResolver.Resolve(
                searchDirectory,
                resolvedProject,
                startupProjectPath?.FullName);
        }
        catch (Exception failure) when (failure is WorkspaceException or InvalidOperationException)
        {
            CliUi.WriteErrorPanel("Workspace failure", failure.Message);
            return 10;
        }

        var displayRoot = Path.GetDirectoryName(
            string.Equals(resolvedProject.FullName, resolvedStartup.FullName, StringComparison.OrdinalIgnoreCase)
                ? resolvedProject.FullName
                : resolvedStartup.FullName)!;

        LinqLiteScanResult scanResult;
        LinqDeepScanStats? deepStats = null;
        var scanMode = mode == LinqScanMode.Deep ? LinqScanMode.Deep : LinqScanMode.Lite;
        string? resolvedDbContextName = null;

        if (mode == LinqScanMode.Lite)
        {
            if (!jsonOutput)
            {
                AnsiConsole.MarkupLine("[dim]Scanning project sources (lite)…[/]");
            }

            scanResult = LinqLiteScanner.Scan(resolvedProject.FullName, resolvedStartup.FullName);
        }
        else
        {
            WorkspaceBuildResult workspaceBuild;
            var pendingSessionDirectory = SessionPaths.EnsurePendingSessionDirectory(workspaceRoot);

            try
            {
                workspaceBuild = CliUi.RunWithStatus(
                    "Building EF project for deep scan…",
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

            using var host = WorkspaceHost.Load(workspaceBuild);

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
                CliUi.WriteErrorPanel("DbContext resolution failed", resolutionFailure.Message);
                return 14;
            }

            resolvedDbContextName = dbContextType.Name;

            var sessionDirectory = SessionPaths.EnsureDbContextSessionDirectory(
                workspaceRoot,
                resolvedProject.FullName,
                resolvedDbContextName);

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
                CliUi.WriteErrorPanel("DbContext resolution failed", resolutionFailure.Message);
                return 14;
            }

            var session = new ScriptSession(
                dbContextInstance.GetType(),
                dbContextInstance,
                workspaceBuild.ReferenceAssemblyPaths,
                host.AssemblyLoader);

            if (!jsonOutput)
                AnsiConsole.MarkupLine("[dim]Scanning project sources and translating SQL (deep)…[/]");

            (scanResult, deepStats) = await LinqDeepScanner.ScanAsync(
                resolvedProject.FullName,
                resolvedStartup.FullName,
                session,
                host,
                progress: null,
                cancellationToken);
        }

        var sessionDirectoryForArtifacts = mode == LinqScanMode.Lite
            ? SessionPaths.EnsureProjectScanDirectory(workspaceRoot, resolvedProject.FullName)
            : SessionPaths.EnsureDbContextSessionDirectory(
                workspaceRoot,
                resolvedProject.FullName,
                resolvedDbContextName ?? contextFullName ?? "DbContext");

        var findings = scanResult.Findings;

        if (respectDismissals)
        {
            (findings, _) = LinqScanDismissalStore.FilterFindings(findings, sessionDirectoryForArtifacts);
        }

        findings = LinqScanCiGate.Filter(findings, minSeverity);

        var filteredResult = new LinqLiteScanResult(
            scanResult.FilesScanned,
            scanResult.ProjectsScanned,
            findings);

        var savedPath = LinqScanSessionFile.Save(
            sessionDirectoryForArtifacts,
            filteredResult,
            displayRoot,
            scanMode,
            deepStats);

        var summary = LinqScanCiGate.Summarize(findings, failOn);

        if (jsonOutput)
            LinqScanCiReporter.WriteJsonSummary(filteredResult, summary, scanMode == LinqScanMode.Deep ? "deep" : "lite", savedPath, deepStats);
        else
            LinqScanCiReporter.WriteTextSummary(summary, scanMode == LinqScanMode.Deep ? "deep" : "lite", savedPath, minSeverity);

        return LinqScanCiGate.GetExitCode(summary);
    }

    private static bool TryParseMode(string? raw, out LinqScanMode mode)
    {
        mode = LinqScanMode.Lite;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (string.Equals(raw, "lite", StringComparison.OrdinalIgnoreCase))
        {
            mode = LinqScanMode.Lite;
            return true;
        }

        if (string.Equals(raw, "deep", StringComparison.OrdinalIgnoreCase))
        {
            mode = LinqScanMode.Deep;
            return true;
        }

        return false;
    }

    private static bool TryParseOptionalSeverity(
        string? raw,
        out LinqScanSeverity? severity,
        out string? error)
    {
        severity = null;
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
            return true;

        if (LinqScanRuleCatalog.TryParseSeverity(raw, out var parsed))
        {
            severity = parsed;
            return true;
        }

        error = $"Unknown severity '{raw}'. Use info, warning, error, or critical.";
        return false;
    }
}
