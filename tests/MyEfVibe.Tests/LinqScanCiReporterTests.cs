using System.Text.Json;
using MyEfVibe.Linq;

namespace MyEfVibe.Tests;

public sealed class LinqScanCiReporterTests
{
    [Fact]
    public void WriteJsonSummary_emits_single_line_json_for_daemon_protocol()
    {
        var result = new LinqLiteScanResult(
            3,
            1,
            [
                new LinqScanFinding(
                    "/tmp/ProductsController.cs",
                    42,
                    "db.Products.ToList()",
                    "client-eval",
                    "Client evaluation detected.",
                    LinqScanSeverity.Warning)
            ]);

        var summary = LinqScanCiGate.Summarize(result.Findings, null);
        var originalOut = Console.Out;

        try
        {
            using var writer = new StringWriter();
            Console.SetOut(writer);

            LinqScanCiReporter.WriteJsonSummary(result, summary, "lite", "/tmp/myefvibe-scan-lite.json");

            var output = writer.ToString().TrimEnd();
            Assert.DoesNotContain('\n', output);
            Assert.DoesNotContain('\r', output);

            using var document = JsonDocument.Parse(output);
            Assert.Equal("lite", document.RootElement.GetProperty("scanMode").GetString());
            Assert.Equal(1, document.RootElement.GetProperty("findings").GetArrayLength());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
