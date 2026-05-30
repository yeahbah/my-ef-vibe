namespace MyEfVibe;

internal sealed class DbLogSettings
{
    internal bool Enabled { get; set; } = true;

    internal DbLogLevel Level { get; set; } = DbLogLevel.Information;

    /// <summary>When false (default), only executed SQL is shown. When true, all EF events at the configured level are shown.</summary>
    internal bool Verbose { get; set; }
}