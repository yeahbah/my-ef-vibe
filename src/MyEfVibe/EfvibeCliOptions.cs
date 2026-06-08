using CommandLine;

namespace MyEfVibe;

internal sealed class EfvibeCliOptions
{
    [Option('w', "workspace",
        HelpText = "Session directory for exports and artifacts. Default: ~/.efvibe or %APPDATA%/efvibe.")]
    public string? Workspace { get; set; }

    [Option('p', "project", HelpText = "EF Core project to build (.csproj with the DbContext).")]
    public string? Project { get; set; }

    [Option('s', "startup-project", HelpText = "Startup project for configuration (user secrets, appsettings).")]
    public string? StartupProject { get; set; }

    [Option('c', "context", HelpText = "DbContext type name or fully qualified name.")]
    public string? Context { get; set; }

    [Option("connection-string|cs", HelpText = "Connection string for manual DbContextOptions construction.")]
    public string? ConnectionString { get; set; }

    [Option("provider",
        HelpText = "Provider with --connection-string: alias (sqlserver, npgsql, sqlite, oracle, mysql, mariadb) or EF package id.")]
    public string? Provider { get; set; }

    [Option('e', "expression", HelpText = "Run a single expression and exit (non-interactive).")]
    public string? Expression { get; set; }

    [Option("dblog", Default = true,
        HelpText = "Enable EF database command logging (default: on; toggle in REPL with :dblog).")]
    public bool DbLog { get; set; }

    [Option("no-dblog", HelpText = "Disable EF database command logging.")]
    public bool NoDbLog { get; set; }

    [Option("dblog-level",
        HelpText = "Database log level: trace | debug | information | warning | error | critical | none.")]
    public string? DbLogLevel { get; set; }

    [Option("dblog-verbose", HelpText = "Show full EF diagnostic logs (default: sql-only executed commands).")]
    public bool DbLogVerbose { get; set; }

    [Option("about-json",
        HelpText = "Write tool metadata as JSON to stdout and exit (no workspace or DbContext required).")]
    public bool AboutJson { get; set; }

    [Option("tables-json", HelpText = "Write DbSet names and entity types as JSON to stdout and exit (no REPL).")]
    public bool TablesJson { get; set; }

    [Option("describe-json",
        HelpText = "Write entity member schema as JSON for the given DbSet or entity type name and exit (no REPL).")]
    public string? DescribeJson { get; set; }

    [Option("dbinfo-json", HelpText = "Write database and connection metadata as JSON to stdout and exit (no REPL).")]
    public bool DbInfoJson { get; set; }

    [Option("completions-json",
        HelpText = "Write db.* completion items for the given prefix (e.g. db.Pro) and exit (no REPL).")]
    public string? CompletionsJson { get; set; }

    [Option("format", HelpText = "Output format for one-shot -e runs: text (default) or json.")]
    public string? Format { get; set; }

    [Option("no-banner", HelpText = "Suppress workspace and build banners (recommended with --format json).")]
    public bool NoBanner { get; set; }

    [Option("with-plan", HelpText = "With -e --format json, include EXPLAIN / SHOWPLAN for the evaluated SQL.")]
    public bool WithPlan { get; set; }

    [Option('f', "framework", HelpText = "Target framework moniker for building the workspace project (e.g. net8.0).")]
    public string? Framework { get; set; }

    [Value(0, MetaName = "expression", HelpText = "Optional one-shot expression when -e is omitted.")]
    public IEnumerable<string>? ExpressionParts { get; set; }
}