namespace MyEfVibe;

/// <summary>
///     Mirrors <c>Microsoft.Extensions.Logging.LogLevel</c> numeric values for EF <c>LogTo</c>.
/// </summary>
internal enum DbLogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
    None = 6
}