using Spectre.Console;

namespace MyEfVibe;

internal static class ScanCommandRunner
{
    internal static Task<int> RunFromOptionsAsync(
        ScanCliOptions options,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            options.Mode,
            CliPathHelper.ResolveWorkspace(options.Workspace),
            CliPathHelper.ToFileInfo(options.Project),
            CliPathHelper.ToFileInfo(options.StartupProject),
            options.Context,
            options.Framework,
            options.FailOn,
            options.MinSeverity,
            options.RespectDismissals,
            options.Json,
            options.NoBanner,
            options.ConnectionString,
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
        bool noBanner,
        string? connectionString,
        CancellationToken cancellationToken = default)
    {
        CliUi.Configure();

        var quietOutput = noBanner || jsonOutput;

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
            if (!quietOutput)
            {
                AnsiConsole.MarkupLine("[dim]Scanning project sources (lite)…[/]");
            }

            scanResult = LinqLiteScanner.Scan(
                resolvedProject.FullName,
                resolvedStartup.FullName,
                selectedContextTypeName: contextFullName);
        }
        else
        {
            WorkspaceBuildResult workspaceBuild;
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
                    false);
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
                    null,
                    false);
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

            if (!quietOutput)
            {
                AnsiConsole.MarkupLine("[dim]Scanning project sources and translating SQL (deep)…[/]");
            }

            (scanResult, deepStats) = await LinqDeepScanner.ScanAsync(
                resolvedProject.FullName,
                resolvedStartup.FullName,
                session,
                host,
                dbContextType,
                null,
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

        var reportMinSeverity = ResolveReportMinSeverity(minSeverity, failOn);
        findings = LinqScanCiGate.Filter(findings, reportMinSeverity);

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
        {
            LinqScanCiReporter.WriteJsonSummary(filteredResult, summary,
                scanMode == LinqScanMode.Deep ? "deep" : "lite", savedPath, deepStats);
        }
        else
        {
            LinqScanCiReporter.WriteTextSummary(summary, scanMode == LinqScanMode.Deep ? "deep" : "lite", savedPath,
                reportMinSeverity);
        }

        return LinqScanCiGate.GetExitCode(summary);
    }

    internal static LinqScanSeverity? ResolveReportMinSeverity(
        LinqScanSeverity? minSeverity,
        LinqScanSeverity? failOn)
    {
        return minSeverity ?? failOn;
    }

    internal static bool TryParseMode(string? raw, out LinqScanMode mode)
    {
        mode = LinqScanMode.Lite;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

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

    internal static bool TryParseOptionalSeverity(
        string? raw,
        out LinqScanSeverity? severity,
        out string? error)
    {
        severity = null;
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (LinqScanRuleCatalog.TryParseSeverity(raw, out var parsed))
        {
            severity = parsed;
            return true;
        }

        error = $"Unknown severity '{raw}'. Use info, warning, error, or critical.";
        return false;
    }
}