namespace MyEfVibe.VisualStudio.Services;

internal sealed class CliRunResult
{
    public int ExitCode { get; set; }
    public string Stdout { get; set; } = string.Empty;
    public string Stderr { get; set; } = string.Empty;

    public bool Succeeded => ExitCode == 0;
}
