using Spectre.Console;

namespace MyEfVibe;

internal static class InteractiveSelection
{
    internal static bool CanPrompt => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    internal static T Choose<T>(
        string title,
        IReadOnlyList<SelectionOption<T>> options)
        where T : notnull
    {
        if (options.Count == 0)
            throw new ArgumentException("At least one option is required.", nameof(options));

        if (options.Count == 1)
            return options[0].Value;

        if (!CanPrompt)
            throw new InvalidOperationException(BuildNonInteractiveMessage(title, options));

        var labels = options.ToDictionary(static option => option.Value);

        return AnsiConsole.Prompt(
            new SelectionPrompt<T>()
                .Title(title)
                .PageSize(ResolvePromptPageSize(options.Count))
                .AddChoices(options.Select(static option => option.Value))
                .UseConverter(value => labels[value].Label));
    }

    internal static int ResolvePromptPageSize(int optionCount) =>
        Math.Max(3, Math.Min(12, optionCount));

    private static string BuildNonInteractiveMessage<T>(string title, IReadOnlyList<SelectionOption<T>> options)
        where T : notnull
    {
        var lines = options.Select(static option => $" - {StripMarkup(option.Label)}").ToArray();

        return $"{StripMarkup(title)}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }

    private static string StripMarkup(string markup) =>
        System.Text.RegularExpressions.Regex.Replace(markup, @"\[[^\]]*\]", string.Empty);
}

internal readonly record struct SelectionOption<T>(T Value, string Label) where T : notnull;
