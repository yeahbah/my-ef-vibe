using Spectre.Console;

namespace MyEfVibe;

internal static class AboutReporter
{
    internal static void Write()
    {
        var tool = new Grid();
        tool.AddColumn();
        tool.AddColumn();
        tool.AddRow("[bold]Command[/]", $"[cyan]{AppMetadata.CommandName}[/] [grey]({Markup.Escape(AppMetadata.ProductName)})[/]");
        tool.AddRow("[grey]Version[/]", Markup.Escape(ToolInfo.GetVersion()));
        tool.AddRow("[grey]Description[/]", Markup.Escape(AppMetadata.GetDescription()));
        tool.AddRow("[grey]Author[/]", Markup.Escape(AppMetadata.GetAuthor()));
        tool.AddRow("[grey]License[/]", AppMetadata.License);
        tool.AddRow("[grey]Website[/]", Markup.Escape(AppMetadata.WebsiteUrl));
        tool.AddRow("[grey]Repository[/]", Markup.Escape(AppMetadata.RepositoryUrl));
        tool.AddRow("[grey]NuGet[/]", Markup.Escape(AppMetadata.NuGetUrl));
        tool.AddRow("[grey]Runtime[/]", Markup.Escape(AppMetadata.GetRuntimeDescription()));

        AnsiConsole.Write(
            new Panel(tool)
            {
                Header = new PanelHeader("[bold]About[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Grey),
                Padding = new Padding(1, 0, 1, 0),
            });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Type :help for commands.[/]");
        AnsiConsole.WriteLine();
    }
}
