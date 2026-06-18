namespace MyEfVibe.Linq;

internal sealed record LinqQuerySite(
    string FilePath,
    int Line,
    string Code,
    string Statement,
    IReadOnlySet<string> ContextInstanceIdentifiers);