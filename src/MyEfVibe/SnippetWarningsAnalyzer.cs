namespace MyEfVibe;

internal static class SnippetWarningsAnalyzer
{
    internal static IReadOnlyList<string> Analyze(string snippet)
        => LinqQueryWarningRules.AnalyzeSnippet(snippet)
            .Select(static warning => warning.Message)
            .ToArray();
}
