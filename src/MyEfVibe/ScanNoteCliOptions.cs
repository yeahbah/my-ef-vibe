using CommandLine;

namespace MyEfVibe;

internal sealed class ScanNoteCliOptions
{
    [Option('w', "workspace", HelpText = "Workspace root for scan artifacts.")]
    public string? Workspace { get; set; }

    [Option('p', "project", Required = true, HelpText = "EF Core project (.csproj).")]
    public string Project { get; set; } = string.Empty;

    [Option('c', "context", HelpText = "DbContext type name (for deep-scan session path).")]
    public string? Context { get; set; }

    [Option("file", Required = true, HelpText = "Finding source file path.")]
    public string File { get; set; } = string.Empty;

    [Option("line", Required = true, HelpText = "Finding line number.")]
    public int Line { get; set; }

    [Option("rule", Required = true, HelpText = "Finding rule id.")]
    public string Rule { get; set; } = string.Empty;

    [Option("text", Required = true, HelpText = "Note text to save.")]
    public string Text { get; set; } = string.Empty;

    [Option("code", HelpText = "Optional finding code snippet (defaults to empty).")]
    public string? Code { get; set; }
}

internal sealed class ScanDismissCliOptions
{
    [Option('w', "workspace", HelpText = "Workspace root for scan artifacts.")]
    public string? Workspace { get; set; }

    [Option('p', "project", Required = true, HelpText = "EF Core project (.csproj).")]
    public string Project { get; set; } = string.Empty;

    [Option('c', "context", HelpText = "DbContext type name (for deep-scan session path).")]
    public string? Context { get; set; }

    [Option("file", Required = true, HelpText = "Finding source file path.")]
    public string File { get; set; } = string.Empty;

    [Option("line", Required = true, HelpText = "Finding line number.")]
    public int Line { get; set; }

    [Option("rule", Required = true, HelpText = "Finding rule id.")]
    public string Rule { get; set; } = string.Empty;

    [Option("note", HelpText = "Optional dismissal note.")]
    public string? Note { get; set; }

    [Option("code", HelpText = "Optional finding code snippet (defaults to empty).")]
    public string? Code { get; set; }
}
