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

    [Option("connection-string|cs", HelpText = "Connection string for manual DbContextOptions construction.")]
    public string? ConnectionString { get; set; }

    [Option("provider", HelpText = "Provider with --connection-string.")]
    public string? Provider { get; set; }

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
}