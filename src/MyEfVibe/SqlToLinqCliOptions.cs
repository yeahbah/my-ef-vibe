using CommandLine;

namespace MyEfVibe;

internal sealed class SqlToLinqCliOptions
{
    [Option('w', "workspace", HelpText = "Workspace root for session artifacts.")]
    public string? Workspace { get; set; }

    [Option('p', "project", HelpText = "EF Core project (.csproj) with the DbContext.")]
    public string? Project { get; set; }

    [Option('s', "startup-project", HelpText = "Startup project for configuration (optional).")]
    public string? StartupProject { get; set; }

    [Option('c', "context", HelpText = "DbContext type name or fully qualified name.")]
    public string? Context { get; set; }

    [Option("connection-string", HelpText = "Connection string for manual DbContextOptions construction.")]
    public string? ConnectionString { get; set; }

    [Option("sql", Required = true, HelpText = "SQL query to convert into an EF-model-aware LINQ draft.")]
    public string Sql { get; set; } = string.Empty;

    [Option("format", HelpText = "Output format: text (default) or json.")]
    public string? Format { get; set; }

    [Option("no-banner", HelpText = "Suppress build/status banners (recommended with --format json).")]
    public bool NoBanner { get; set; }

    [Option('f', "framework", HelpText = "Target framework moniker for building the project (e.g. net10.0).")]
    public string? Framework { get; set; }

    [Option("dblog", Default = true, HelpText = "Enable EF database command logging.")]
    public bool DbLog { get; set; }

    [Option("no-dblog", HelpText = "Disable EF database command logging.")]
    public bool NoDbLog { get; set; }

    [Option("dblog-level", HelpText = "Database log level.")]
    public string? DbLogLevel { get; set; }

    [Option("dblog-verbose", HelpText = "Show full EF diagnostic logs.")]
    public bool DbLogVerbose { get; set; }
}
