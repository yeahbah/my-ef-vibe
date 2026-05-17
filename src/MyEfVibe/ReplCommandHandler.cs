namespace MyEfVibe;

internal sealed class ReplCommandHandler
{
    private readonly ScriptSession _session;
    private readonly WorkspaceHost _host;
    private readonly object _dbContext;
    private readonly SqlDisplaySettings _sqlSettings;
    private readonly SessionAnalytics _analytics;
    private readonly InputHistory _history;

    internal ReplCommandHandler(
        ScriptSession session,
        WorkspaceHost host,
        object dbContext,
        SqlDisplaySettings sqlSettings,
        SessionAnalytics analytics,
        InputHistory history)
    {
        _session = session;
        _host = host;
        _dbContext = dbContext;
        _sqlSettings = sqlSettings;
        _analytics = analytics;
        _history = history;
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

            case "describe":
            case "desc":
                EntityDescriptor.Write(_dbContext, string.Join(' ', parts.Skip(1)));
                return true;

            case "plan":
                await QueryPlanRunner.WritePlanAsync(
                    _dbContext,
                    _analytics.LastMetrics?.TranslatedSql,
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
