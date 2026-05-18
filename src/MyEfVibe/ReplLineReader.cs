using System.Text;

namespace MyEfVibe;

internal sealed class ReplLineReader
{
    private readonly InputHistory _history;
    private readonly ReplCompletionService? _completion;
    private readonly LinqScanReviewSession? _scanReview;
    private readonly Queue<string> _pendingRecalledLines = new();
    [ThreadStatic]
    private static int _lastRenderedLineCount;

    internal ReplLineReader(
        InputHistory history,
        ReplCompletionService? completion = null,
        LinqScanReviewSession? scanReview = null)
    {
        _history = history;
        _completion = completion;
        _scanReview = scanReview;
    }

    internal bool HasPendingRecalledLines => _pendingRecalledLines.Count > 0;

    internal IReadOnlyList<string> DequeueAllPendingRecalledLines()
    {
        var lines = new List<string>();

        while (_pendingRecalledLines.Count > 0)
            lines.Add(_pendingRecalledLines.Dequeue());

        return lines;
    }

    internal string? ReadLine(string prompt)
    {
        if (_pendingRecalledLines.Count > 0)
        {
            CliUi.WritePrompt(prompt);

            var recalled = _pendingRecalledLines.Dequeue();

            Console.WriteLine(recalled);

            return recalled;
        }

        if (Console.IsInputRedirected)
        {
            CliUi.WritePrompt(prompt);

            var line = Console.ReadLine();

            return line is null ? null : InputLineUtilities.NormalizeNewlines(line);
        }

        return ReadLineInteractive(prompt);
    }

    internal void RecordSubmission(string snippet) => _history.Add(snippet);

    private string? ReadLineInteractive(string prompt)
    {
        CliUi.WritePrompt(prompt);

        var buffer = new StringBuilder();
        var cursor = 0;

        _history.ResetNavigation();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
                    {
                        buffer.Insert(cursor, '\n');
                        cursor++;

                        RenderMultiline(prompt, buffer, cursor);

                        break;
                    }

                    Console.WriteLine();

                    return InputLineUtilities.NormalizeNewlines(buffer.ToString());

                case ConsoleKey.UpArrow:
                    if (_history.TryNavigateUp(out var upEntry))
                        ApplyRecalledEntry(upEntry, prompt, buffer, ref cursor);
                    else
                        Console.Beep();

                    break;

                case ConsoleKey.DownArrow:
                    if (_history.TryNavigateDown(out var downEntry))
                        ApplyRecalledEntry(downEntry, prompt, buffer, ref cursor);
                    else
                        ReplaceBuffer(prompt, buffer, string.Empty, ref cursor);

                    break;

                case ConsoleKey.Backspace:
                    if (cursor <= 0)
                        break;

                    buffer.Remove(cursor - 1, 1);
                    cursor--;

                    RenderMultiline(prompt, buffer, cursor);

                    break;

                case ConsoleKey.Delete:
                    if (TryScanReviewDismiss(prompt, buffer, ref cursor))
                        break;

                    if (cursor >= buffer.Length)
                        break;

                    buffer.Remove(cursor, 1);

                    RenderMultiline(prompt, buffer, cursor);

                    break;

                case ConsoleKey.LeftArrow:
                    if (TryScanReviewNavigatePrevious(prompt, buffer, ref cursor))
                        break;

                    if (cursor > 0)
                    {
                        Console.Write('\b');

                        cursor--;
                    }

                    break;

                case ConsoleKey.RightArrow:
                    if (TryScanReviewNavigateNext(prompt, buffer, ref cursor))
                        break;

                    if (cursor < buffer.Length)
                    {
                        Console.Write(buffer[cursor]);

                        cursor++;
                    }

                    break;

                case ConsoleKey.Home:
                    cursor = 0;

                    RenderMultiline(prompt, buffer, cursor);

                    break;

                case ConsoleKey.End:
                    cursor = buffer.Length;

                    RenderMultiline(prompt, buffer, cursor);

                    break;

                case ConsoleKey.Escape:
                    buffer.Clear();
                    cursor = 0;

                    RenderMultiline(prompt, buffer, cursor);

                    break;

                case ConsoleKey.Tab:
                    if (_completion is not null)
                        TryApplyCompletion(prompt, buffer, ref cursor);
                    else
                        Console.Beep();

                    break;

                default:
                    if (char.IsControl(key.KeyChar))
                    {
                        if (key.KeyChar == '\r')
                        {
                            buffer.Insert(cursor, '\n');
                            cursor++;
                            RenderMultiline(prompt, buffer, cursor);
                        }

                        break;
                    }

                    buffer.Insert(cursor, key.KeyChar);
                    cursor++;

                    RenderMultiline(prompt, buffer, cursor);

                    break;
            }
        }
    }

    private bool TryScanReviewNavigateNext(string prompt, StringBuilder buffer, ref int cursor)
    {
        if (_scanReview?.IsActive != true || buffer.Length > 0 || cursor > 0)
            return false;

        _scanReview.TryNext();
        RenderMultiline(ResolvePrompt(prompt), buffer, cursor);

        return true;
    }

    private bool TryScanReviewNavigatePrevious(string prompt, StringBuilder buffer, ref int cursor)
    {
        if (_scanReview?.IsActive != true || buffer.Length > 0 || cursor > 0)
            return false;

        _scanReview.TryPrevious();
        RenderMultiline(ResolvePrompt(prompt), buffer, cursor);

        return true;
    }

    private bool TryScanReviewDismiss(string prompt, StringBuilder buffer, ref int cursor)
    {
        if (_scanReview?.IsActive != true || buffer.Length > 0 || cursor > 0)
            return false;

        _scanReview.TryDismiss(note: null);
        RenderMultiline(ResolvePrompt(prompt), buffer, cursor);

        return true;
    }

    private string ResolvePrompt(string fallbackPrompt) =>
        _scanReview?.GetActivePrompt() ?? fallbackPrompt;

    private void ApplyRecalledEntry(string entry, string prompt, StringBuilder buffer, ref int cursor)
    {
        var lines = InputLineUtilities.SplitLines(entry);

        ReplaceBuffer(prompt, buffer, lines[0], ref cursor);

        _pendingRecalledLines.Clear();

        for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
            _pendingRecalledLines.Enqueue(lines[lineIndex]);
    }

    private static void ReplaceBuffer(string prompt, StringBuilder buffer, string text, ref int cursor)
    {
        buffer.Clear();
        buffer.Append(text);
        cursor = buffer.Length;

        RenderMultiline(prompt, buffer, cursor);
    }

    private void TryApplyCompletion(string prompt, StringBuilder buffer, ref int cursor)
    {
        var line = buffer.ToString();

        IReadOnlyList<CompletionSuggestion> suggestions;

        try
        {
            suggestions = _completion!
                .GetSuggestionsAsync(line, cursor)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception failure)
        {
            CliUi.WriteWarning($"Completion error: {failure.Message}");

            return;
        }

        if (suggestions.Count == 0)
            return;

        var partial = GetCompletionPrefix(line, cursor);

        var matches = suggestions
            .Where(suggestion => partial.Length == 0
                || suggestion.InsertText.StartsWith(partial, StringComparison.OrdinalIgnoreCase)
                || suggestion.DisplayText.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
            return;

        if (matches.Length == 1)
        {
            ApplySuggestion(prompt, buffer, ref cursor, matches[0]);

            return;
        }

        var commonPrefix = LongestCommonPrefix(matches.Select(static match => match.InsertText));

        if (commonPrefix.Length > partial.Length)
        {
            var extension = commonPrefix[partial.Length..];
            var merged = matches[0] with
            {
                InsertText = extension,
                ReplaceStart = cursor - partial.Length,
                ReplaceLength = partial.Length,
            };

            ApplySuggestion(prompt, buffer, ref cursor, merged);

            return;
        }

        ShowSuggestionList(prompt, buffer, cursor, matches);
    }

    private static void ApplySuggestion(
        string prompt,
        StringBuilder buffer,
        ref int cursor,
        CompletionSuggestion suggestion)
    {
        if (suggestion.InsertText.Length == 0 && suggestion.ReplaceLength == 0)
            return;

        var replaceStart = Math.Clamp(suggestion.ReplaceStart, 0, buffer.Length);
        var replaceEnd = Math.Clamp(replaceStart + suggestion.ReplaceLength, replaceStart, buffer.Length);

        if (replaceEnd > replaceStart)
            buffer.Remove(replaceStart, replaceEnd - replaceStart);

        buffer.Insert(replaceStart, suggestion.InsertText);
        cursor = replaceStart + suggestion.InsertText.Length;

        RenderMultiline(prompt, buffer, cursor);
    }

    private static void ShowSuggestionList(
        string prompt,
        StringBuilder buffer,
        int cursor,
        IReadOnlyList<CompletionSuggestion> matches)
    {
        Spectre.Console.AnsiConsole.WriteLine();

        foreach (var match in matches.Take(12))
            Spectre.Console.AnsiConsole.MarkupLine($"  [grey]•[/] [cyan]{Spectre.Console.Markup.Escape(match.DisplayText)}[/]");

        if (matches.Count > 12)
            Spectre.Console.AnsiConsole.MarkupLine($"  [grey]… and {matches.Count - 12} more[/]");

        Spectre.Console.AnsiConsole.WriteLine();

        RenderMultiline(prompt, buffer, cursor);
    }

    private static string GetCompletionPrefix(string line, int cursor)
    {
        var tokenStart = FindIdentifierStart(line, cursor);
        var segment = line[tokenStart..cursor];
        var lastDot = segment.LastIndexOf('.');

        return lastDot >= 0 ? segment[(lastDot + 1)..] : segment;
    }

    private static int FindIdentifierStart(string line, int cursor)
    {
        var index = Math.Min(cursor, line.Length) - 1;

        while (index >= 0 && (char.IsLetterOrDigit(line[index]) || line[index] is '_' or '.'))
            index--;

        return index + 1;
    }

    private static string LongestCommonPrefix(IEnumerable<string> values)
    {
        var list = values.ToArray();

        if (list.Length == 0)
            return string.Empty;

        var prefix = list[0];

        foreach (var value in list.Skip(1))
        {
            var length = 0;
            var max = Math.Min(prefix.Length, value.Length);

            while (length < max && prefix[length] == value[length])
                length++;

            prefix = prefix[..length];

            if (prefix.Length == 0)
                break;
        }

        return prefix;
    }

    private static void RenderMultiline(string primaryPrompt, StringBuilder buffer, int cursor)
    {
        var text = buffer.ToString();
        var lines = text.Length == 0 ? new[] { string.Empty } : InputLineUtilities.SplitLines(text);

        var lineIndex = 0;
        var column = 0;
        var consumed = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            if (cursor <= consumed + lines[index].Length)
            {
                lineIndex = index;
                column = cursor - consumed;

                break;
            }

            consumed += lines[index].Length + 1;
            lineIndex = index;
            column = lines[index].Length;
        }

        if (_lastRenderedLineCount > 1)
            Console.Write($"\x1b[{_lastRenderedLineCount - 1}A");

        for (var index = 0; index < _lastRenderedLineCount; index++)
        {
            Console.Write("\r\x1b[2K");

            if (index < _lastRenderedLineCount - 1)
                Console.WriteLine();
        }

        if (_lastRenderedLineCount > 1)
            Console.Write($"\x1b[{_lastRenderedLineCount - 1}A");

        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
                Console.WriteLine();

            var linePrompt = index == 0 ? primaryPrompt : CliUi.ContinuationPrompt;
            CliUi.WritePrompt(linePrompt);
            Console.Write(lines[index]);
        }

        _lastRenderedLineCount = Math.Max(1, lines.Length);

        var linesBelow = lines.Length - 1 - lineIndex;

        if (linesBelow > 0)
            Console.Write($"\x1b[{linesBelow}A");

        var activePrompt = lineIndex == 0 ? primaryPrompt : CliUi.ContinuationPrompt;
        Console.Write("\r");
        CliUi.WritePrompt(activePrompt);
        Console.Write(lines[lineIndex]);

        var tailLength = lines[lineIndex].Length - column;

        if (tailLength > 0)
            Console.Write(new string('\b', tailLength));
    }
}
