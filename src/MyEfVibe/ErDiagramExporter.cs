using System.Text;
using MyEfVibe.Reporters;

namespace MyEfVibe;

internal static class ErDiagramExporter
{
    internal static void Export(
        object dbContext,
        string sessionDirectory,
        string? entityName = null,
        string? path = null)
    {
        var payload = DiagramJsonReporter.Build(dbContext, entityName);

        if (!payload.Success || string.IsNullOrWhiteSpace(payload.Content))
        {
            CliUi.WriteWarning(payload.Error ?? "Could not build ER diagram.");

            if (payload.KnownEntities is { Length: > 0 })
            {
                CliUi.WriteWarning("Known entities: " + string.Join(", ", payload.KnownEntities));
            }

            return;
        }

        var targetPath = SessionPaths.ResolveDiagramExportPath(
            sessionDirectory,
            path,
            dbContext.GetType().Name,
            entityName);

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, payload.Content, Encoding.UTF8);

        if (!string.IsNullOrWhiteSpace(payload.DbSet))
        {
            CliUi.WriteSuccess(
                $"Exported ER diagram for {payload.DbSet} ({payload.EntityType}) to {targetPath}");
            return;
        }

        CliUi.WriteSuccess($"Exported ER diagram to {targetPath}");
    }

    internal static bool LooksLikeExportPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (Path.IsPathRooted(value))
        {
            return true;
        }

        if (value.StartsWith('.') || value.Contains('/') || value.Contains('\\'))
        {
            return true;
        }

        return value.EndsWith(".mmd", StringComparison.OrdinalIgnoreCase);
    }
}
