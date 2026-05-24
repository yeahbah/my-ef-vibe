using System.Reflection;
using System.Runtime.InteropServices;
using Spectre.Console;

namespace MyEfVibe;

internal static class AboutReporter
{
    private const string WebsiteUrl = "https://myefvibe.com";
    private const string RepositoryUrl = "https://github.com/yeahbah/my-ef-vibe";
    private const string NuGetUrl = "https://www.nuget.org/packages/efvibe";

    internal static void Write(object dbContext, WorkspaceHost host)
    {
        var assembly = typeof(AboutReporter).Assembly;
        var contextType = dbContext.GetType();

        var tool = new Grid();
        tool.AddColumn();
        tool.AddColumn();
        tool.AddRow("[bold]Command[/]", "[cyan]efvibe[/] [grey](MyEfVibe)[/]");
        tool.AddRow("[grey]Version[/]", Markup.Escape(ToolInfo.GetVersion()));
        tool.AddRow("[grey]Description[/]", Markup.Escape(GetDescription(assembly)));
        tool.AddRow("[grey]Author[/]", Markup.Escape(GetAuthors(assembly)));
        tool.AddRow("[grey]License[/]", "Apache-2.0");
        tool.AddRow("[grey]Website[/]", Markup.Escape(WebsiteUrl));
        tool.AddRow("[grey]Repository[/]", Markup.Escape(RepositoryUrl));
        tool.AddRow("[grey]NuGet[/]", Markup.Escape(NuGetUrl));
        tool.AddRow("[grey]Runtime[/]", Markup.Escape(GetRuntimeDescription()));

        AnsiConsole.Write(
            new Panel(tool)
            {
                Header = new PanelHeader("[bold]About[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Grey),
                Padding = new Padding(1, 0, 1, 0),
            });

        AnsiConsole.WriteLine();

        var session = new Grid();
        session.AddColumn();
        session.AddColumn();
        session.AddRow("[grey]DbContext[/]", $"[cyan]{Markup.Escape(contextType.FullName ?? contextType.Name)}[/]");
        session.AddRow("[grey]EF project[/]", Markup.Escape(FormatProjectPath(host.ProjectPath)));

        if (!string.Equals(host.ProjectPath, host.StartupProjectPath, StringComparison.OrdinalIgnoreCase))
            session.AddRow("[grey]Startup project[/]", Markup.Escape(FormatProjectPath(host.StartupProjectPath)));

        session.AddRow("[grey]Session directory[/]", Markup.Escape(host.SessionDirectory));

        var efVersion = TryGetEfCoreVersion();

        if (efVersion is not null)
            session.AddRow("[grey]EF Core[/]", Markup.Escape(efVersion));

        AnsiConsole.Write(
            new Panel(session)
            {
                Header = new PanelHeader("[bold]Current session[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Grey),
                Padding = new Padding(1, 0, 1, 0),
            });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Type :help for commands.[/]");
        AnsiConsole.WriteLine();
    }

    private static string GetDescription(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description
        ?? "Interactive EF Core LINQ REPL for external projects.";

    private static string GetAuthors(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company
        ?? "Arnold Diaz";

    private static string GetRuntimeDescription()
    {
        var framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        var rid = RuntimeInformation.RuntimeIdentifier;

        return string.IsNullOrWhiteSpace(rid) ? framework : $"{framework} ({rid})";
    }

    private static string? TryGetEfCoreVersion()
    {
        var efAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal));

        return efAssembly?.GetName().Version?.ToString(3);
    }

    private static string FormatProjectPath(string absolutePath)
    {
        try
        {
            return Path.GetRelativePath(Directory.GetCurrentDirectory(), absolutePath);
        }
        catch
        {
            return absolutePath;
        }
    }
}
