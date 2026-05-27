namespace MyEfVibe;

internal static class ServeScanner
{
    internal static async Task WriteJsonScanAsync(
        WorkspaceRuntime runtime,
        string modeRaw,
        bool respectDismissals,
        string? minSeverityRaw,
        CancellationToken cancellationToken = default)
    {
        if (!ScanCommandRunner.TryParseMode(modeRaw, out var mode))
            throw new InvalidOperationException("scan mode must be lite or deep.");

        if (!ScanCommandRunner.TryParseOptionalSeverity(minSeverityRaw, out var minSeverity, out var minSeverityError))
            throw new InvalidOperationException(minSeverityError ?? "Invalid scan severity.");

        var host = runtime.Host;
        var resolvedProject = host.ProjectPath;
        var resolvedStartup = host.StartupProjectPath;
        var workspaceRoot = runtime.WorkspaceRoot;

        var displayRoot = Path.GetDirectoryName(
            string.Equals(resolvedProject, resolvedStartup, StringComparison.OrdinalIgnoreCase)
                ? resolvedProject
                : resolvedStartup)!;

        LinqLiteScanResult scanResult;
        LinqDeepScanStats? deepStats = null;
        var scanMode = mode == LinqScanMode.Deep ? LinqScanMode.Deep : LinqScanMode.Lite;

        if (mode == LinqScanMode.Lite)
        {
            scanResult = LinqLiteScanner.Scan(
                resolvedProject,
                resolvedStartup,
                selectedContextTypeName: runtime.DbContextName);
        }
        else
        {
            runtime.Host.EnsureEntityFrameworkRelationalLoaded();
            runtime.Host.EnsureAspNetCoreSharedFrameworkLoaded();

            (scanResult, deepStats) = await LinqDeepScanner.ScanAsync(
                resolvedProject,
                resolvedStartup,
                runtime.Session,
                runtime.Host,
                runtime.DbContext.GetType(),
                progress: null,
                cancellationToken);
        }

        var sessionDirectoryForArtifacts = mode == LinqScanMode.Lite
            ? SessionPaths.EnsureProjectScanDirectory(workspaceRoot, resolvedProject)
            : SessionPaths.EnsureDbContextSessionDirectory(
                workspaceRoot,
                resolvedProject,
                runtime.DbContextName);

        var findings = scanResult.Findings;

        if (respectDismissals)
            (findings, _) = LinqScanDismissalStore.FilterFindings(findings, sessionDirectoryForArtifacts);

        var reportMinSeverity = ScanCommandRunner.ResolveReportMinSeverity(minSeverity, failOn: null);
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

        var summary = LinqScanCiGate.Summarize(findings, null);

        LinqScanCiReporter.WriteJsonSummary(
            filteredResult,
            summary,
            scanMode == LinqScanMode.Deep ? "deep" : "lite",
            savedPath,
            deepStats);
    }
}
