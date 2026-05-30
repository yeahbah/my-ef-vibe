namespace MyEfVibe;

internal static class SnippetWarningsAnalyzer
{
    internal static IReadOnlyList<string> Analyze(string snippet)
    {
        return LinqQueryWarningRules.AnalyzeSnippet(snippet)
            .Select(static warning => warning.Message)
            .ToArray();
    }
}