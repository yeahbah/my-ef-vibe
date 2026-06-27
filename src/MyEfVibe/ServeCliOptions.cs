using CommandLine;

namespace MyEfVibe;

internal sealed class ServeCliOptions
{
    [Option('w', "workspace", HelpText = "Session directory for exports and artifacts.")]
    public string? Workspace { get; set; }

    [Option('p', "project", Required = true, HelpText = "EF Core project to build (.csproj with the DbContext).")]
    public string? Project { get; set; }

    [Option('s', "startup-project", HelpText = "Startup project for configuration.")]
    public string? StartupProject { get; set; }

    [Option('c', "context", HelpText = "DbContext type name or fully qualified name.")]
    public string? Context { get; set; }

    [Option("connection-string", HelpText = "Connection string for manual DbContextOptions construction.")]
    public string? ConnectionString { get; set; }

    [Option("dblog", Default = true, HelpText = "Enable EF database command logging.")]
    public bool DbLog { get; set; }

    [Option("no-dblog", HelpText = "Disable EF database command logging.")]
    public bool NoDbLog { get; set; }

    [Option("dblog-level", HelpText = "Database log level.")]
    public string? DbLogLevel { get; set; }

    [Option("dblog-verbose", HelpText = "Show full EF diagnostic logs.")]
    public bool DbLogVerbose { get; set; }

    [Option('f', "framework", HelpText = "Target framework moniker (e.g. net8.0).")]
    public string? Framework { get; set; }

    [Option("no-build",
        HelpText = "Reuse isolated build output; fail when it is missing or stale (default: rebuild only when inputs changed).")]
    public bool NoBuild { get; set; }

    [Option("force-build", HelpText = "Always run dotnet build, even when isolated output is still fresh.")]
    public bool ForceBuild { get; set; }

    [Option("script-search-path", Separator = ';', HelpText = "Directory for resolving #load paths and relative script files.")]
    public IEnumerable<string>? ScriptSearchPath { get; set; }

    [Option("script-load", Separator = ';', HelpText = "Script file(s) to #load when the session starts (semicolon-separated).")]
    public IEnumerable<string>? ScriptLoad { get; set; }

    [Option("script-using", Separator = ';', HelpText = "Additional namespace(s) to import in the script session (semicolon-separated).")]
    public IEnumerable<string>? ScriptUsing { get; set; }
}