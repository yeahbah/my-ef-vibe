namespace MyEfVibe;

/// <summary>
///     During <c>efvibe serve</c>, JSON protocol lines must be the only writes to stdout.
///     User/factory <see cref="Console.WriteLine" /> calls are redirected to stderr.
/// </summary>
internal sealed class ServeConsoleGuard : IDisposable
{
    private static TextWriter? _protocolOut;
    private readonly TextWriter _previousOut;

    private ServeConsoleGuard(TextWriter previousOut) => _previousOut = previousOut;

    internal static ServeConsoleGuard Enter()
    {
        var protocolOut = Console.Out;
        Console.SetOut(Console.Error);
        _protocolOut = protocolOut;

        return new ServeConsoleGuard(protocolOut);
    }

    internal static TextWriter ProtocolOut => _protocolOut ?? Console.Out;

    public void Dispose()
    {
        Console.SetOut(_previousOut);
        _protocolOut = null;
    }
}
