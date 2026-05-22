using CommandLine;
using Spectre.Console;

namespace MyEfVibe;

internal static class CliParseHelper
{
    internal static int PrintErrorsAndReturnFailure(IEnumerable<Error> errors)
    {
        if (errors.Any(static error =>
                error.Tag is ErrorType.HelpRequestedError or ErrorType.VersionRequestedError))
            return 0;

        CliUi.Configure();

        foreach (var error in errors)
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error.ToString() ?? string.Empty)}[/]");

        return 1;
    }
}
