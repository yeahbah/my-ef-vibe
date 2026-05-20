namespace MyEfVibe;

/// <summary>
/// Severity for LINQ scan findings. Higher values are more severe (used for CI thresholds).
/// </summary>
public enum LinqScanSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3,
}
