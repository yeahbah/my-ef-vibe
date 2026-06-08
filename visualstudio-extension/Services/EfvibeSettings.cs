using MyEfVibe.VisualStudio.Options;

namespace MyEfVibe.VisualStudio.Services;

internal sealed class EfvibeSettings
{
    public string WorkspaceRoot { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string StartupProject { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public string ToolPath { get; set; } = string.Empty;
    public string DotnetFramework { get; set; } = string.Empty;
    public bool DbLog { get; set; } = true;
    public bool ScanRespectDismissals { get; set; } = true;
    public string ScanMinSeverity { get; set; } = string.Empty;

    public static EfvibeSettings FromOptions(EfvibeOptionsPage options, string solutionDirectory)
    {
        return new EfvibeSettings
        {
            WorkspaceRoot = PathResolver.ResolvePath(options.WorkspaceRoot, solutionDirectory),
            Project = PathResolver.ResolvePath(options.Project, solutionDirectory),
            StartupProject = PathResolver.ResolvePath(options.StartupProject, solutionDirectory),
            Context = options.Context?.Trim() ?? string.Empty,
            ConnectionString = options.ConnectionString?.Trim() ?? string.Empty,
            ToolPath = PathResolver.ResolvePath(options.ToolPath, solutionDirectory),
            DotnetFramework = options.DotnetFramework?.Trim() ?? string.Empty,
            DbLog = options.DbLog,
            ScanRespectDismissals = options.ScanRespectDismissals,
            ScanMinSeverity = options.ScanMinSeverity?.Trim() ?? string.Empty,
        };
    }
}
