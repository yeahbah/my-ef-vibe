namespace MyEfVibe;

/// <summary>
///     Redirects <see cref="Console.Out"/> during script evaluation so user output is captured for JSON
///     results and does not corrupt daemon line-delimited stdout.
/// </summary>
internal sealed class ScriptConsoleCapture : IDisposable
{
    private readonly TextWriter _previousOut;
    private readonly StringWriter _writer = new();

    internal ScriptConsoleCapture()
    {
        _previousOut = Console.Out;
        Console.SetOut(_writer);
    }

    internal string CapturedOutput => _writer.ToString().TrimEnd();

    public void Dispose()
    {
        Console.SetOut(_previousOut);
    }
}
