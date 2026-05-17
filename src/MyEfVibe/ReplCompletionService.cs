namespace MyEfVibe;

internal sealed record CompletionSuggestion(
    string DisplayText,
    string InsertText,
    int ReplaceStart,
    int ReplaceLength);

internal sealed class ReplCompletionService
{
    private static readonly string[] Keywords =
    [
        "db",
        "Where",
        "Select",
        "OrderBy",
        "ThenBy",
        "GroupBy",
        "Join",
        "Include",
        "First",
        "FirstOrDefault",
        "Single",
        "SingleOrDefault",
        "ToList",
        "ToListAsync",
        "Count",
        "CountAsync",
        "Any",
        "All",
        "Take",
        "Skip",
        "Distinct",
        "AsNoTracking",
        "AsQueryable",
    ];

    internal Task<IReadOnlyList<CompletionSuggestion>> GetSuggestionsAsync(
        string currentLine,
        int cursorPosition,
        CancellationToken cancellationToken = default)
    {
        if (cursorPosition < 0 || cursorPosition > currentLine.Length)
            return Task.FromResult<IReadOnlyList<CompletionSuggestion>>(Array.Empty<CompletionSuggestion>());

        var wordStart = cursorPosition;

        while (wordStart > 0 && IsIdentifierPart(currentLine[wordStart - 1]))
            wordStart--;

        var prefix = currentLine[wordStart..cursorPosition];

        if (prefix.Length == 0)
            return Task.FromResult<IReadOnlyList<CompletionSuggestion>>(Array.Empty<CompletionSuggestion>());

        var suggestions = Keywords
            .Where(keyword => keyword.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(keyword => new CompletionSuggestion(
                keyword,
                keyword,
                wordStart,
                prefix.Length))
            .ToArray();

        return Task.FromResult<IReadOnlyList<CompletionSuggestion>>(suggestions);
    }

    private static bool IsIdentifierPart(char character)
        => char.IsLetterOrDigit(character) || character is '_' or '@';
}
