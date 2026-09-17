using System.Collections.Generic;

namespace MyEfVibe.VisualStudio.Services;

/// <summary>
///     Converts Visual Studio <see cref="System.Diagnostics.Process.OutputDataReceived"/> payloads into
///     daemon protocol lines.
/// </summary>
/// <remarks>
///     <see cref="System.Diagnostics.Process.BeginOutputReadLine"/> already splits stdout on newlines and
///     raises one event per line with the terminator stripped. Re-scanning those payloads for <c>\n</c>
///     (the VS Code / Rider raw-chunk approach) never completes a line, so the <c>{"type":"ready"}</c>
///     handshake hangs until the 10-minute timeout.
/// </remarks>
internal static class DaemonStdoutLineParser
{
    internal static IEnumerable<string> CompleteLinesFromBeginOutputReadLine(string? eventData)
    {
        if (string.IsNullOrEmpty(eventData))
        {
            yield break;
        }

        var line = eventData.Trim();

        if (line.Length > 0)
        {
            yield return line;
        }
    }
}
