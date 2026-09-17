using MyEfVibe.VisualStudio.Services;

namespace MyEfVibe.Tests;

public sealed class DaemonStdoutLineParserTests
{
    [Fact]
    public void CompleteLinesFromBeginOutputReadLine_treats_payload_without_newline_as_a_complete_line()
    {
        const string ready = """{"type":"ready","dbContext":"AppDbContext"}""";

        Assert.DoesNotContain('\n', ready);
        Assert.Equal(ready, Assert.Single(DaemonStdoutLineParser.CompleteLinesFromBeginOutputReadLine(ready)));
    }

    [Fact]
    public void CompleteLinesFromBeginOutputReadLine_ignores_null_and_blank_events()
    {
        Assert.Empty(DaemonStdoutLineParser.CompleteLinesFromBeginOutputReadLine(null));
        Assert.Empty(DaemonStdoutLineParser.CompleteLinesFromBeginOutputReadLine(""));
        Assert.Empty(DaemonStdoutLineParser.CompleteLinesFromBeginOutputReadLine("   "));
    }

    [Fact]
    public void Raw_chunk_newline_scan_never_completes_a_BeginOutputReadLine_payload()
    {
        const string ready = """{"type":"ready","dbContext":"AppDbContext"}""";

        Assert.Empty(ScanRawChunksForNewlines(ready));
    }

    private static IReadOnlyList<string> ScanRawChunksForNewlines(string chunk)
    {
        var lines = new List<string>();
        var index = chunk.IndexOf('\n');

        if (index < 0)
        {
            return lines;
        }

        var line = chunk[..index].Trim('\r', '\n', ' ');

        if (line.Length > 0)
        {
            lines.Add(line);
        }

        return lines;
    }
}
