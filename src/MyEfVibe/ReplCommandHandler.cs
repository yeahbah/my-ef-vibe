using Spectre.Console;

namespace MyEfVibe;

internal sealed class ReplCommandHandler
{
    private readonly ScriptSession _session;
    private readonly WorkspaceHost _host;
    private readonly object _dbContext;
    private readonly DbLogSettings _dbLogSettings;
    private readonly SessionAnalytics _analytics;
    private readonly InputHistory _history;
    private readonly LinqScanReviewSession _scanReview;

    internal ReplCommandHandler(
        ScriptSession session,
        WorkspaceHost host,
        object dbContext,
        DbLogSettings dbLogSettings,
        SessionAnalytics analytics,
        InputHistory history,
        LinqScanReviewSession scanReview)
    {
        _session = session;
        _host = host;
        _dbContext = dbContext;
        _dbLogSettings = dbLogSettings;
        _analytics = analytics;
        _history = history;
        _scanReview = scanReview;
    }

    internal async Task<bool> TryHandleAsync(string command, CancellationToken cancellationToken)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var name = parts[0].ToLowerInvariant();

        switch (name)
        {
            case "stats":
                AnalyticsPresenter.WriteSessionStats(_analytics.Evaluations);
                return true;

            case "tracked":
            case "track":
                ChangeTrackerReporter.Write(_dbContext);
                return true;

            case "tables":
                await SchemaBrowser.WriteTablesAsync(_dbContext, cancellationToken);
                return true;

            case "dbinfo":
                await DbInfoReporter.WriteAsync(_dbContext, _host, cancellationToken);
                return true;

            case "about":
                AboutReporter.Write();
                return true;

            case "describe":
            case "desc":
                EntityDescriptor.Write(_dbContext, string.Join(' ', parts.Skip(1)));
                return true;

            case "scan":
                await HandleScanAsync(parts, cancellationToken);
                return true;

            case "next":
                if (!_scanReview.IsActive)
                    return false;

                _scanReview.TryNext();
                return true;

            case "prev":
            case "previous":
                if (!_scanReview.IsActive)
                    return false;

                _scanReview.TryPrevious();
                return true;

            case "repeat":
                _scanReview.GoToStart();
                return true;

            case "end":
                _scanReview.End();
                return true;

            case "dismiss":
                if (!_scanReview.IsActive)
                {
                    CliUi.WriteWarning("No scan review in progress.");
                    return true;
                }

                var dismissNote = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : null;
                _scanReview.TryDismiss(dismissNote);
                return true;

            case "note":
                if (!_scanReview.IsActive)
                {
                    CliUi.WriteWarning("No scan review in progress.");
                    return true;
                }

                if (parts.Length < 2)
                {
                    CliUi.WriteWarning("Usage: :note <text>");
                    return true;
                }

                _scanReview.TrySetNote(string.Join(' ', parts.Skip(1)));
                return true;

            case "dblog":
                HandleDbLog(parts);
                return true;

            case "plan":
                await QueryPlanRunner.WritePlanAsync(
                    _dbContext,
                    AnalyticsPresenter.GetPlanSql(_analytics.LastMetrics),
                    _host.EnumerateLoadedAssemblies(),
                    cancellationToken);
                return true;

            case "compare":
                HandleCompare(parts);
                return true;

            case "history":
                if (parts.Length > 1 && parts[1].Equals("stats", StringComparison.OrdinalIgnoreCase))
                    AnalyticsPresenter.WriteHistoryStats(_history.Entries, _analytics.Evaluations);
                else
                    CliUi.WriteWarning("Usage: :history stats");

                return true;

            case "benchmark":
                await HandleBenchmarkAsync(parts, cancellationToken);
                return true;

            case "export":
                HandleExport(parts);
                return true;

            case "warnings":
                if (_analytics.LastMetrics is null)
                    CliUi.WriteWarning("No evaluations yet.");
                else
                    AnalyticsPresenter.WriteWarnings(_analytics.LastMetrics.Warnings);

                return true;

            case "chart":
            case "viz":
                await HandleChartAsync(parts, cancellationToken);
                return true;

            default:
                return false;
        }
    }

    private async Task HandleScanAsync(string[] parts, CancellationToken cancellationToken)
    {
        if (parts.Length < 2)
        {
            LinqScanPresenter.WriteUsage();
            return;
        }

        var mode = parts[1].ToLowerInvariant();
        var displayRoot = Path.GetDirectoryName(
            string.Equals(_host.ProjectPath, _host.StartupProjectPath, StringComparison.OrdinalIgnoreCase)
                ? _host.ProjectPath
                : _host.StartupProjectPath)!;

        switch (mode)
        {
            case "lite":
                HandleScanLite(displayRoot);
                break;

            case "deep":
                await HandleScanDeepAsync(displayRoot, cancellationToken);
                break;

            default:
                LinqScanPresenter.WriteUsage();
                break;
        }
    }

    private void HandleScanLite(string displayRoot)
    {
        var result = CliUi.RunWithStatus(
            "Scanning project sources for LINQ patterns…",
            () => LinqLiteScanner.Scan(
                _host.ProjectPath,
                _host.StartupProjectPath,
                _session.DbContext.GetType()));

        var (filteredFindings, dismissedSkipped) = LinqScanDismissalStore.FilterFindings(
            result.Findings,
            _host.SessionDirectory);

        var findingsWithNotes = LinqScanNoteStore.ApplyNotes(filteredFindings, _host.SessionDirectory);

        var filteredResult = new LinqLiteScanResult(
            result.FilesScanned,
            result.ProjectsScanned,
            findingsWithNotes);

        if (filteredResult.Findings.Count == 0)
        {
            LinqScanPresenter.WriteLiteSummary(filteredResult, displayRoot, string.Empty, dismissedSkipped);
            return;
        }

        _scanReview.Begin(filteredResult, _host.SessionDirectory, displayRoot, dismissedSkippedCount: dismissedSkipped);
    }

    private async Task HandleScanDeepAsync(string displayRoot, CancellationToken cancellationToken)
    {
        LinqLiteScanResult? result = null;
        LinqDeepScanStats? stats = null;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots2)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync(
                "Scanning project sources and translating SQL…",
                async context =>
                {
                    var progress = new Progress<(int Completed, int Total)>(update =>
                    {
                        if (update.Total == 0)
                            return;

                        context.Status(
                            $"Translating SQL ({update.Completed}/{update.Total})…");
                    });

                    (result, stats) = await LinqDeepScanner.ScanAsync(
                        _host.ProjectPath,
                        _host.StartupProjectPath,
                        _session,
                        _host,
                        _session.DbContext.GetType(),
                        progress,
                        cancellationToken);
                });

        if (result is null)
            return;

        var (filteredFindings, dismissedSkipped) = LinqScanDismissalStore.FilterFindings(
            result.Findings,
            _host.SessionDirectory);

        var findingsWithNotes = LinqScanNoteStore.ApplyNotes(filteredFindings, _host.SessionDirectory);

        var filteredResult = new LinqLiteScanResult(
            result.FilesScanned,
            result.ProjectsScanned,
            findingsWithNotes);

        if (filteredResult.Findings.Count == 0)
        {
            LinqScanPresenter.WriteDeepSummary(filteredResult, displayRoot, string.Empty, stats, dismissedSkipped);
            return;
        }

        _scanReview.Begin(
            filteredResult,
            _host.SessionDirectory,
            displayRoot,
            LinqScanMode.Deep,
            stats,
            dismissedSkipped);
    }

    private void HandleDbLog(string[] parts)
    {
        if (parts.Length == 1)
        {
            CliUi.WriteSuccess($"Database logging is {DbLogCommandParser.FormatStatus(_dbLogSettings)}.");
            return;
        }

        switch (parts[1].ToLowerInvariant())
        {
            case "status":
                CliUi.WriteSuccess($"Database logging is {DbLogCommandParser.FormatStatus(_dbLogSettings)}.");
                return;

            case "on":
                _dbLogSettings.Enabled = true;
                _dbLogSettings.Verbose = false;

                if (parts.Length >= 3
                    && !DbLogCommandParser.TryApplyOnArguments(parts[2..], _dbLogSettings, out var error))
                {
                    CliUi.WriteWarning(error ?? "Invalid :dblog options.");
                    return;
                }

                CliUi.WriteSuccess($"Database logging is {DbLogCommandParser.FormatStatus(_dbLogSettings)}.");
                return;

            case "off":
                _dbLogSettings.Enabled = false;
                _dbLogSettings.Verbose = false;
                CliUi.WriteSuccess("Database logging is off.");
                return;

            default:
                CliUi.WriteWarning(
                    "Usage: :dblog [on|off [level] [verbose]] — default is sql-only; add verbose for full EF logs.");
                return;
        }
    }

    private void HandleCompare(string[] parts)
    {
        if (parts.Length > 1 && parts[1].Equals("set", StringComparison.OrdinalIgnoreCase))
        {
            _analytics.SetCompareBaseline();
            CliUi.WriteSuccess("Comparison baseline set to the last evaluation.");
            return;
        }

        if (parts.Length > 1 && parts[1].Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            _analytics.ClearCompareBaseline();
            CliUi.WriteSuccess("Comparison baseline cleared.");
            return;
        }

        if (_analytics.CompareBaseline is null || _analytics.LastMetrics is null)
        {
            CliUi.WriteWarning(
                "Run a query, then `:compare set` for a baseline, run another query, and use `:compare` again.");
            return;
        }

        AnalyticsPresenter.WriteCompare(_analytics.CompareBaseline, _analytics.LastMetrics);
    }

    private async Task HandleBenchmarkAsync(string[] parts, CancellationToken cancellationToken)
    {
        if (_analytics.LastSnippet is null)
        {
            CliUi.WriteWarning("Run a query first, then `:benchmark 5` to repeat it.");
            return;
        }

        var iterations = 5;

        if (parts.Length > 1 && !int.TryParse(parts[1], out iterations))
        {
            CliUi.WriteWarning("Usage: :benchmark [iterations]");
            return;
        }

        await BenchmarkRunner.RunAsync(_dbContext, _session, _analytics.LastSnippet, iterations, cancellationToken);
    }

    private async Task HandleChartAsync(string[] parts, CancellationToken cancellationToken)
    {
        if (parts.Length < 2)
        {
            VisualizationPresenter.WriteHelp();
            return;
        }

        switch (parts[1].ToLowerInvariant())
        {
            case "stats":
                VisualizationPresenter.WriteSessionTimings(_analytics.Evaluations);
                break;

            case "timing":
            case "time":
                VisualizationPresenter.WriteLastTimingBreakdown(_analytics.LastMetrics);
                break;

            case "compare":
                VisualizationPresenter.WriteCompare(_analytics.CompareBaseline, _analytics.LastMetrics);
                break;

            case "tables":
                VisualizationPresenter.WriteTableRowCounts(
                    await SchemaBrowser.GetDbSetCountsAsync(_dbContext, cancellationToken));
                break;

            case "result":
            case "rows":
                VisualizationPresenter.WriteResultNumeric(_analytics.ExportRows);
                break;

            default:
                CliUi.WriteWarning("Usage: :chart stats|timing|compare|tables|result");
                break;
        }
    }

    private void HandleExport(string[] parts)
    {
        if (parts.Length < 2)
        {
            CliUi.WriteWarning("Usage: :export csv|json [path]");
            return;
        }

        var format = parts[1];
        var path = parts.Length > 2 ? parts[2] : null;

        ResultExporter.Export(_analytics.ExportRows, _host.SessionDirectory, format, path);
    }
}
