using CommandLine;

namespace MyEfVibe;

internal sealed class ScanCliOptions
{
    [Value(0, MetaName = "mode", Required = true, HelpText = "Scan mode: lite (static heuristics) or deep (heuristics + SQL translation).")]
    public string Mode { get; set; } = string.Empty;

    [Option('w', "workspace", HelpText = "Workspace root for scan artifacts.")]
    public string? Workspace { get; set; }

    [Option('p', "project", HelpText = "EF Core project (.csproj) to scan.")]
    public string? Project { get; set; }

    [Option('s', "startup-project", HelpText = "Startup project for configuration (optional).")]
    public string? StartupProject { get; set; }

    [Option('c', "context", HelpText = "DbContext type name (required for deep scan when multiple contexts exist).")]
    public string? Context { get; set; }

    [Option('f', "framework", HelpText = "Target framework moniker for building the project (e.g. net10.0).")]
    public string? Framework { get; set; }

    [Option("fail-on", HelpText = "Exit 1 when any finding has this severity or higher (info | warning | error | critical).")]
    public string? FailOn { get; set; }

    [Option("min-severity", HelpText = "Only report findings at or above this severity.")]
    public string? MinSeverity { get; set; }

    [Option("respect-dismissals", Default = false, HelpText = "Exclude findings previously dismissed in the REPL session.")]
    public bool RespectDismissals { get; set; }

    [Option("json", Default = false, HelpText = "Write scan summary and findings as JSON to stdout.")]
    public bool Json { get; set; }

    [Option("connection-string|cs", HelpText = "Connection string for deep scan (requires --provider).")]
    public string? ConnectionString { get; set; }

    [Option("provider", HelpText = "Database provider for deep scan with --connection-string.")]
    public string? Provider { get; set; }
}
