namespace MyEfVibe.IntegrationTests;

public enum CliCommandKind
{
    Main,
    Serve,
    ScanLite,
    ScanDeep,
    ScanNote,
    ScanDismiss,
    LanguageServer
}

internal sealed record CliSwitchCase(
    CliCommandKind Command,
    string Name,
    IReadOnlyList<string> ExtraArguments);

internal static class CliSwitchCatalog
{
    internal static IEnumerable<CliSwitchCase> All() =>
        MainSwitches()
            .Concat(ServeSwitches())
            .Concat(ScanLiteSwitches())
            .Concat(ScanDeepSwitches())
            .Concat(ScanNoteSwitches())
            .Concat(ScanDismissSwitches())
            .Concat(LanguageServerSwitches());

    internal static IEnumerable<CliSwitchCase> MainSwitches()
    {
        yield return Case(CliCommandKind.Main, "workspace-long", "--workspace", Placeholders.Workspace);
        yield return Case(CliCommandKind.Main, "workspace-short", "-w", Placeholders.Workspace);
        yield return Case(CliCommandKind.Main, "project-long", "--project", Placeholders.EfProject);
        yield return Case(CliCommandKind.Main, "project-short", "-p", Placeholders.EfProject);
        yield return Case(CliCommandKind.Main, "startup-long", "--startup-project", Placeholders.StartupProject);
        yield return Case(CliCommandKind.Main, "startup-short", "-s", Placeholders.StartupProject);
        yield return Case(CliCommandKind.Main, "context-long", "--context", Placeholders.Context);
        yield return Case(CliCommandKind.Main, "context-short", "-c", Placeholders.Context);
        yield return Case(CliCommandKind.Main, "connection-string", "--connection-string", Placeholders.ConnectionString);
        yield return Case(CliCommandKind.Main, "expression-long", "--expression", "db.Products.Count()");
        yield return Case(CliCommandKind.Main, "expression-short", "-e", "db.Products.Count()");
        yield return Flag(CliCommandKind.Main, "dblog", "--dblog");
        yield return Flag(CliCommandKind.Main, "no-dblog", "--no-dblog");
        yield return Case(CliCommandKind.Main, "dblog-level", "--dblog-level", "warning");
        yield return Flag(CliCommandKind.Main, "dblog-verbose", "--dblog-verbose");
        yield return Flag(CliCommandKind.Main, "tables-json", "--tables-json");
        yield return Case(CliCommandKind.Main, "describe-json", "--describe-json", "Products");
        yield return Flag(CliCommandKind.Main, "dbinfo-json", "--dbinfo-json");
        yield return Case(CliCommandKind.Main, "completions-json", "--completions-json", "db.Pro");
        yield return Case(CliCommandKind.Main, "format", "--format", "json");
        yield return Flag(CliCommandKind.Main, "no-banner", "--no-banner");
        yield return Flag(CliCommandKind.Main, "with-plan", "--with-plan");
        yield return Case(CliCommandKind.Main, "framework-long", "--framework", Placeholders.Framework);
        yield return Case(CliCommandKind.Main, "framework-short", "-f", Placeholders.Framework);
        yield return Case(CliCommandKind.Main, "positional-expression", "db.Products.Count()");
    }

    internal static IEnumerable<CliSwitchCase> ServeSwitches()
    {
        yield return Case(CliCommandKind.Serve, "workspace-long", "--workspace", Placeholders.Workspace);
        yield return Case(CliCommandKind.Serve, "workspace-short", "-w", Placeholders.Workspace);
        yield return Case(CliCommandKind.Serve, "startup-long", "--startup-project", Placeholders.StartupProject);
        yield return Case(CliCommandKind.Serve, "startup-short", "-s", Placeholders.StartupProject);
        yield return Case(CliCommandKind.Serve, "context-long", "--context", Placeholders.Context);
        yield return Case(CliCommandKind.Serve, "context-short", "-c", Placeholders.Context);
        yield return Case(CliCommandKind.Serve, "connection-string", "--connection-string", Placeholders.ConnectionString);
        yield return Flag(CliCommandKind.Serve, "dblog", "--dblog");
        yield return Flag(CliCommandKind.Serve, "no-dblog", "--no-dblog");
        yield return Case(CliCommandKind.Serve, "dblog-level", "--dblog-level", "warning");
        yield return Flag(CliCommandKind.Serve, "dblog-verbose", "--dblog-verbose");
        yield return Case(CliCommandKind.Serve, "framework-long", "--framework", Placeholders.Framework);
        yield return Case(CliCommandKind.Serve, "framework-short", "-f", Placeholders.Framework);
    }

    internal static IEnumerable<CliSwitchCase> ScanLiteSwitches()
    {
        yield return Case(CliCommandKind.ScanLite, "workspace-long", "--workspace", Placeholders.Workspace);
        yield return Case(CliCommandKind.ScanLite, "workspace-short", "-w", Placeholders.Workspace);
        yield return Case(CliCommandKind.ScanLite, "startup-long", "--startup-project", Placeholders.StartupProject);
        yield return Case(CliCommandKind.ScanLite, "startup-short", "-s", Placeholders.StartupProject);
        yield return Case(CliCommandKind.ScanLite, "context-long", "--context", Placeholders.Context);
        yield return Case(CliCommandKind.ScanLite, "context-short", "-c", Placeholders.Context);
        yield return Case(CliCommandKind.ScanLite, "framework-long", "--framework", Placeholders.Framework);
        yield return Case(CliCommandKind.ScanLite, "framework-short", "-f", Placeholders.Framework);
        yield return Case(CliCommandKind.ScanLite, "fail-on", "--fail-on", "error");
        yield return Case(CliCommandKind.ScanLite, "min-severity", "--min-severity", "warning");
        yield return Flag(CliCommandKind.ScanLite, "respect-dismissals", "--respect-dismissals");
        yield return Flag(CliCommandKind.ScanLite, "json", "--json");
        yield return Flag(CliCommandKind.ScanLite, "no-banner", "--no-banner");
        yield return Case(CliCommandKind.ScanLite, "connection-string", "--connection-string", Placeholders.ConnectionString);
    }

    internal static IEnumerable<CliSwitchCase> ScanDeepSwitches()
    {
        yield return Case(CliCommandKind.ScanDeep, "workspace-long", "--workspace", Placeholders.Workspace);
        yield return Case(CliCommandKind.ScanDeep, "workspace-short", "-w", Placeholders.Workspace);
        yield return Case(CliCommandKind.ScanDeep, "startup-long", "--startup-project", Placeholders.StartupProject);
        yield return Case(CliCommandKind.ScanDeep, "startup-short", "-s", Placeholders.StartupProject);
        yield return Case(CliCommandKind.ScanDeep, "context-long", "--context", Placeholders.Context);
        yield return Case(CliCommandKind.ScanDeep, "context-short", "-c", Placeholders.Context);
        yield return Case(CliCommandKind.ScanDeep, "framework-long", "--framework", Placeholders.Framework);
        yield return Case(CliCommandKind.ScanDeep, "framework-short", "-f", Placeholders.Framework);
        yield return Case(CliCommandKind.ScanDeep, "fail-on", "--fail-on", "critical");
        yield return Case(CliCommandKind.ScanDeep, "min-severity", "--min-severity", "info");
        yield return Flag(CliCommandKind.ScanDeep, "respect-dismissals", "--respect-dismissals");
        yield return Flag(CliCommandKind.ScanDeep, "json", "--json");
        yield return Flag(CliCommandKind.ScanDeep, "no-banner", "--no-banner");
        yield return Case(CliCommandKind.ScanDeep, "connection-string", "--connection-string", Placeholders.ConnectionString);
    }

    internal static IEnumerable<CliSwitchCase> ScanNoteSwitches()
    {
        yield return Case(CliCommandKind.ScanNote, "workspace-long", "--workspace", Placeholders.Workspace);
        yield return Case(CliCommandKind.ScanNote, "workspace-short", "-w", Placeholders.Workspace);
        yield return Case(CliCommandKind.ScanNote, "context-long", "--context", Placeholders.Context);
        yield return Case(CliCommandKind.ScanNote, "context-short", "-c", Placeholders.Context);
        yield return Case(CliCommandKind.ScanNote, "file", "--file", Placeholders.ScanFile);
        yield return Case(CliCommandKind.ScanNote, "line", "--line", "1");
        yield return Case(CliCommandKind.ScanNote, "rule", "--rule", "test-rule");
        yield return Case(CliCommandKind.ScanNote, "text", "--text", "integration note");
        yield return Case(CliCommandKind.ScanNote, "code", "--code", "db.Products.ToList()");
    }

    internal static IEnumerable<CliSwitchCase> ScanDismissSwitches()
    {
        yield return Case(CliCommandKind.ScanDismiss, "workspace-long", "--workspace", Placeholders.Workspace);
        yield return Case(CliCommandKind.ScanDismiss, "workspace-short", "-w", Placeholders.Workspace);
        yield return Case(CliCommandKind.ScanDismiss, "context-long", "--context", Placeholders.Context);
        yield return Case(CliCommandKind.ScanDismiss, "context-short", "-c", Placeholders.Context);
        yield return Case(CliCommandKind.ScanDismiss, "file", "--file", Placeholders.ScanFile);
        yield return Case(CliCommandKind.ScanDismiss, "line", "--line", "1");
        yield return Case(CliCommandKind.ScanDismiss, "rule", "--rule", "test-rule");
        yield return Case(CliCommandKind.ScanDismiss, "note", "--note", "dismissed in integration test");
        yield return Case(CliCommandKind.ScanDismiss, "code", "--code", "db.Products.ToList()");
    }

    internal static IEnumerable<CliSwitchCase> LanguageServerSwitches()
    {
        yield return Case(CliCommandKind.LanguageServer, "workspace-long", "--workspace", Placeholders.Workspace);
        yield return Case(CliCommandKind.LanguageServer, "workspace-short", "-w", Placeholders.Workspace);
        yield return Case(CliCommandKind.LanguageServer, "startup-long", "--startup-project", Placeholders.StartupProject);
        yield return Case(CliCommandKind.LanguageServer, "startup-short", "-s", Placeholders.StartupProject);
        yield return Case(CliCommandKind.LanguageServer, "context-long", "--context", Placeholders.Context);
        yield return Case(CliCommandKind.LanguageServer, "context-short", "-c", Placeholders.Context);
        yield return Case(CliCommandKind.LanguageServer, "connection-string", "--connection-string", Placeholders.ConnectionString);
        yield return Case(CliCommandKind.LanguageServer, "framework-long", "--framework", Placeholders.Framework);
        yield return Case(CliCommandKind.LanguageServer, "framework-short", "-f", Placeholders.Framework);
    }

    private static CliSwitchCase Case(CliCommandKind command, string name, params string[] extraArguments) =>
        new(command, name, extraArguments);

    private static CliSwitchCase Flag(CliCommandKind command, string name, string flag) =>
        new(command, name, [flag]);

    internal static class Placeholders
    {
        internal const string Workspace = "{workspace}";
        internal const string EfProject = "{efProject}";
        internal const string StartupProject = "{startupProject}";
        internal const string Context = "{context}";
        internal const string ConnectionString = "{connectionString}";
        internal const string Framework = "{framework}";
        internal const string ScanFile = "{scanFile}";
    }
}
