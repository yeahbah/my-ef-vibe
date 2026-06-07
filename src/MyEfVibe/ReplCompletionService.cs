using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;

namespace MyEfVibe;

internal sealed record CompletionSuggestion(
    string DisplayText,
    string InsertText,
    int ReplaceStart,
    int ReplaceLength);

internal sealed class ReplCompletionService
{
    private static readonly MefHostServices MefHost = MefHostServices.Create(MefHostServices.DefaultAssemblies);

    private readonly ScriptSession _session;

    internal ReplCompletionService(ScriptSession session)
    {
        _session = session;
    }

    internal async Task<IReadOnlyList<CompletionSuggestion>> GetSuggestionsAsync(
        string currentLine,
        int cursorPosition,
        CancellationToken cancellationToken = default)
    {
        if (cursorPosition < 0 || cursorPosition > currentLine.Length)
        {
            return Array.Empty<CompletionSuggestion>();
        }

        var (source, position, lineStartInDocument) = _session.CreateCompletionSource(currentLine, cursorPosition);

        var workspace = new AdhocWorkspace(MefHost);

        var project = workspace
            .AddProject("MyEfVibeRepl", LanguageNames.CSharp)
            .AddMetadataReferences(_session.MetadataReferences)
            .WithCompilationOptions(_session.CompilationOptions);

        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var document = project.AddDocument("repl.cs", await syntaxTree.GetRootAsync(cancellationToken));

        var completionService = CompletionService.GetService(document);

        if (completionService is null)
        {
            return [];
        }

        var completionList = await completionService.GetCompletionsAsync(
            document,
            position,
            options: null,
            cancellationToken: cancellationToken);

        if (completionList is null)
        {
            return [];
        }

        var suggestions = new List<CompletionSuggestion>();

        foreach (var item in completionList.ItemsList)
        {
            var displayText = item.DisplayText;

            if (string.IsNullOrWhiteSpace(displayText))
            {
                continue;
            }

            var change = await completionService.GetChangeAsync(document, item, cancellationToken: cancellationToken);
            var span = change.TextChange.Span;
            var replaceStart = Math.Clamp(span.Start - lineStartInDocument, 0, currentLine.Length);
            var replaceEnd = Math.Clamp(span.End - lineStartInDocument, replaceStart, currentLine.Length);
            var insertText = change.TextChange.NewText;

            if (string.IsNullOrEmpty(insertText))
            {
                insertText = displayText;
            }

            if (!IsUsableInsertText(insertText))
            {
                continue;
            }

            suggestions.Add(new CompletionSuggestion(
                displayText,
                insertText,
                replaceStart,
                replaceEnd - replaceStart));
        }

        return suggestions
            .DistinctBy(static suggestion => $"{suggestion.ReplaceStart}:{suggestion.ReplaceLength}:{suggestion.InsertText}")
            .OrderBy(static suggestion => suggestion.DisplayText, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsUsableInsertText(string insertText)
    {
        return insertText.Length is > 0 and <= 128
               && !insertText.Contains('\n')
               && !insertText.Contains('\r')
               && !insertText.Contains('{');
    }
}
