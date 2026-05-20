using Spectre.Console;

namespace MyEfVibe;

internal sealed class QueryRepl
{
    private readonly ScriptSession _session;
    private readonly WorkspaceHost _host;
    private readonly object _dbContext;
    private readonly string _contextTypeName;
    private readonly string _projectLabel;
    private readonly DbLogSettings _dbLogSettings;
    private readonly SessionAnalytics _analytics = new();
    private readonly InputHistory _history = new();
    private readonly LinqScanReviewSession _scanReview = new();
    private readonly ReplLineReader _lineReader;
    private readonly ReplCommandHandler _commands;

    internal QueryRepl(
        ScriptSession session,
        WorkspaceHost host,
        object dbContext,
        DbLogSettings dbLogSettings,
        string projectLabel)
    {
        _session = session;
        _host = host;
        _dbContext = dbContext;
        _contextTypeName = dbContext.GetType().Name;
        _projectLabel = projectLabel;
        _dbLogSettings = dbLogSettings;
        _lineReader = new ReplLineReader(_history, new ReplCompletionService(), _scanReview);
        _commands = new ReplCommandHandler(session, host, dbContext, dbLogSettings, _analytics, _history, _scanReview);
    }

    internal async Task RunAsync(CancellationToken cancellationToken = default)
    {
        WriteBanner();

        while (!cancellationToken.IsCancellationRequested)
        {
            string? snippet;

            try
            {
                snippet = await ReadSnippetAsync(cancellationToken);
            }
            catch (EndOfStreamException)
            {
                AnsiConsole.WriteLine();
                break;
            }

            if (snippet is null)
                break;

            if (string.IsNullOrWhiteSpace(snippet))
                continue;

            var trimmedSnippet = snippet.Trim();

            var (handled, shouldExit) = await TryHandleReplCommandAsync(trimmedSnippet, cancellationToken);

            if (handled)
            {
                if (shouldExit)
                    break;

                continue;
            }

            await EvaluateAndPrintAsync(snippet, cancellationToken);
        }

        CliUi.WriteGoodbye();
    }

    private async Task<string?> ReadSnippetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? firstLine;

        try
        {
            firstLine = _lineReader.ReadLine(_scanReview.GetActivePrompt() ?? CliUi.PrimaryPrompt);
        }
        catch (EndOfStreamException)
        {
            throw;
        }

        if (firstLine is null)
            return null;

        if (string.IsNullOrWhiteSpace(firstLine) && !_lineReader.HasPendingRecalledLines)
            return string.Empty;

        var trimmedFirst = firstLine.Trim();

        if (trimmedFirst.StartsWith(':'))
            return trimmedFirst;

        if (EndsWithSubmitSemicolon(firstLine))
            return JoinSnippetLines(SplitInputLines(firstLine));

        // Enter adds a line; `;` ends input and runs.
        var buffer = SplitInputLines(firstLine)
            .Select(NormalizeInputLine)
            .Where(static line => line.Length > 0)
            .ToList();

        if (_lineReader.HasPendingRecalledLines)
            buffer.AddRange(_lineReader.DequeueAllPendingRecalledLines());

        var submitSeen = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_lineReader.HasPendingRecalledLines)
            {
                buffer.AddRange(_lineReader.DequeueAllPendingRecalledLines());
                continue;
            }

            string? continuation;

            try
            {
                continuation = _lineReader.ReadLine(CliUi.ContinuationPrompt);
            }
            catch (EndOfStreamException)
            {
                break;
            }

            if (continuation is null)
                break;

            if (SnippetInputClassifier.IsSubmitOnlyLine(continuation))
            {
                submitSeen = true;

                break;
            }

            if (EndsWithSubmitSemicolon(continuation))
            {
                buffer.Add(NormalizeInputLine(continuation));
                submitSeen = true;

                break;
            }

            if (!string.IsNullOrWhiteSpace(continuation))
                buffer.Add(NormalizeInputLine(continuation));
        }

        if (!submitSeen)
        {
            if (buffer.Count > 0)
                CliUi.WriteWarning("Input incomplete — end with `;` to run.");

            return string.Empty;
        }

        return JoinSnippetLines(buffer);
    }

    private static bool EndsWithSubmitSemicolon(string line) =>
        InputLineUtilities.TrimLineEnd(line).EndsWith(';');

    private static string[] SplitInputLines(string input) =>
        InputLineUtilities.SplitLines(input);

    private static string JoinSnippetLines(IEnumerable<string> lines) =>
        InputLineUtilities.JoinLines(lines);

    private static string NormalizeInputLine(string line) =>
        InputLineUtilities.TrimLineEnd(line);

    private void WriteBanner()
    {
        CliUi.WriteBanner();
        CliUi.WriteSessionPanel(_contextTypeName, _projectLabel, _dbLogSettings);
        CliUi.WriteRule("repl");
    }

    private async Task<(bool Handled, bool Exit)> TryHandleReplCommandAsync(string line, CancellationToken cancellationToken)
    {
        if (!line.StartsWith(':'))
            return (false, false);

        var command = line[1..].Trim();

        switch (command.ToLowerInvariant())
        {
            case "":
                return (true, false);

            case "q":
            case "quit":
            case "exit":
                return (true, true);

            case "help":
            case "h":
            case "?":
                CliUi.WriteHelpTable();
                return (true, false);

            case "clear":
            case "cls":
                CliUi.ClearScreen();
                return (true, false);

            case "reset":
                _session.Reset();
                CliUi.WriteSuccess("Script state cleared. `db` is unchanged.");
                return (true, false);

            default:
                if (await _commands.TryHandleAsync(command, cancellationToken))
                    return (true, false);

                CliUi.WriteWarning($"Unknown command `:{command}`. Type :help.");
                return (true, false);
        }
    }

    private async Task EvaluateAndPrintAsync(string snippet, CancellationToken cancellationToken)
    {
        try
        {
            await QueryResultWriter.WriteEvaluationAsync(
                _dbContext,
                _session,
                snippet,
                _dbLogSettings,
                _host,
                _analytics,
                cancellationToken: cancellationToken);

            _lineReader.RecordSubmission(snippet);
        }
        catch (EvaluationFailedException evaluationFailure)
        {
            AnalyticsPresenter.WriteFooter(evaluationFailure.Metrics);
            AnalyticsPresenter.WriteWarnings(evaluationFailure.Metrics.Warnings);
            CliUi.WriteErrorPanel("Evaluation error", evaluationFailure.Message);
            _session.Reset();
            _lineReader.RecordSubmission(snippet);
        }
        catch (CompilationEvaluationException compilationFailure)
        {
            CliUi.WriteErrorPanel("Compilation error", compilationFailure.Message);
            _session.Reset();
            _lineReader.RecordSubmission(snippet);
        }
        catch (Exception failure)
        {
            CliUi.WriteErrorPanel("Runtime error", failure.Message);
            _session.Reset();
            _lineReader.RecordSubmission(snippet);
        }

        AnsiConsole.WriteLine();
    }
}
