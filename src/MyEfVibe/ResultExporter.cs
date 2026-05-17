using System.Text;

namespace MyEfVibe;

internal static class ResultExporter
{
    internal static void Export(
        IReadOnlyList<object?> rows,
        string sessionDirectory,
        string format,
        string? path = null)
    {
        if (rows.Count == 0)
        {
            CliUi.WriteWarning("Nothing to export from the last result.");
            return;
        }

        var targetPath = SessionPaths.ResolveExportPath(sessionDirectory, path, format);
        var content = format.Equals("json", StringComparison.OrdinalIgnoreCase)
            ? TabularExportBuilder.ToJson(rows)
            : TabularExportBuilder.ToCsv(rows);

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, content, Encoding.UTF8);
        CliUi.WriteSuccess($"Exported {rows.Count} row(s) to {targetPath}");
    }
}
