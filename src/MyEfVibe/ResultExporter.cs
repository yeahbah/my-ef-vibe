using System.Text;

namespace MyEfVibe;

internal static class ResultExporter
{
    internal static void Export(IReadOnlyList<object?> rows, string format, string? path = null)
    {
        if (rows.Count == 0)
        {
            CliUi.WriteWarning("Nothing to export from the last result.");
            return;
        }

        var targetPath = path ?? BuildDefaultPath(format);
        var content = format.Equals("json", StringComparison.OrdinalIgnoreCase)
            ? TabularExportBuilder.ToJson(rows)
            : TabularExportBuilder.ToCsv(rows);

        File.WriteAllText(targetPath, content, Encoding.UTF8);
        CliUi.WriteSuccess($"Exported {rows.Count} row(s) to {targetPath}");
    }

    private static string BuildDefaultPath(string format)
    {
        var extension = format.Equals("json", StringComparison.OrdinalIgnoreCase) ? "json" : "csv";
        var fileName = $"myefvibe-export-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}";

        return Path.Combine(Environment.CurrentDirectory, fileName);
    }
}
