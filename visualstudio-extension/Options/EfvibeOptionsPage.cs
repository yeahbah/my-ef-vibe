using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace MyEfVibe.VisualStudio.Options;

public sealed class EfvibeOptionsPage : DialogPage
{
    [Category("CLI")]
    [DisplayName("Workspace root")]
    [Description("Session root for exports and scan artifacts. Empty uses efvibe's default.")]
    public string WorkspaceRoot { get; set; } = string.Empty;

    [Category("Project")]
    [DisplayName("EF project")]
    [Description("Path to the EF Core project (.csproj) with the DbContext. Relative paths resolve from the solution directory.")]
    public string Project { get; set; } = string.Empty;

    [Category("Project")]
    [DisplayName("Startup project")]
    [Description("Startup project for appsettings and user secrets. Relative paths resolve from the solution directory.")]
    public string StartupProject { get; set; } = string.Empty;

    [Category("Project")]
    [DisplayName("DbContext")]
    [Description("DbContext type name or fully qualified name.")]
    public string Context { get; set; } = string.Empty;

    [Category("Connection")]
    [DisplayName("Connection string")]
    [Description("Optional connection string passed to efvibe --connection-string.")]
    public string ConnectionString { get; set; } = string.Empty;

    [Category("CLI")]
    [DisplayName("Tool path")]
    [Description("Optional full path to efvibe/myefvibe. Empty uses local dotnet tool manifest or global efvibe on PATH.")]
    public string ToolPath { get; set; } = string.Empty;

    [Category("CLI")]
    [DisplayName("Target framework")]
    [Description("Optional target framework moniker for building the EF project, for example net8.0.")]
    public string DotnetFramework { get; set; } = string.Empty;

    [Category("CLI")]
    [DisplayName("Database logging")]
    [Description("Enable EF database command logging.")]
    public bool DbLog { get; set; } = true;

    [Category("Scan")]
    [DisplayName("Respect dismissals")]
    [Description("Exclude findings previously dismissed in the efvibe session.")]
    public bool ScanRespectDismissals { get; set; } = true;

    [Category("Scan")]
    [DisplayName("Minimum severity")]
    [Description("Optional scan severity filter: info, warning, error, or critical.")]
    public string ScanMinSeverity { get; set; } = string.Empty;
}
