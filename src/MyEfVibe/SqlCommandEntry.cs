namespace MyEfVibe;

internal sealed record SqlCommandEntry(
    string Text,
    long? DurationMilliseconds,
    IReadOnlyList<string> Parameters);
