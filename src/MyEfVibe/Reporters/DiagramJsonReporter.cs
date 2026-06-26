using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyEfVibe.Reporters;

internal static class DiagramJsonReporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    internal static void Write(object dbContext, string? entityName = null)
    {
        var payload = Build(dbContext, entityName);
        Console.WriteLine(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    internal static DiagramJsonPayload Build(object dbContext, string? entityName = null)
    {
        string? resolvedDbSet = null;
        string? resolvedEntityType = null;

        if (!string.IsNullOrWhiteSpace(entityName))
        {
            var dbSets = EntityDescriptor.EnumerateDbSetEntities(dbContext).ToArray();

            switch (EntityDescriptor.TryResolveEntity(dbSets, entityName.Trim(), out var resolved))
            {
                case EntityDescriptor.EntityResolveResult.NotFound:
                    return new DiagramJsonPayload
                    {
                        Success = false,
                        DbContext = dbContext.GetType().Name,
                        Format = "mermaid",
                        Error = $"Entity `{entityName}` was not found.",
                        KnownEntities = dbSets
                            .Select(static entry => $"{entry.DbSetName} · {entry.EntityType.Name}")
                            .ToArray()
                    };

                case EntityDescriptor.EntityResolveResult.Ambiguous:
                    return new DiagramJsonPayload
                    {
                        Success = false,
                        DbContext = dbContext.GetType().Name,
                        Format = "mermaid",
                        Error = $"Multiple entities match `{entityName}`. Use a DbSet name or full type name.",
                        KnownEntities = resolved.AmbiguousMatches!
                            .Select(static entry => $"{entry.DbSetName} · {entry.EntityType.Name}")
                            .ToArray()
                    };

                case EntityDescriptor.EntityResolveResult.Found:
                    resolvedDbSet = resolved.Match!.Value.DbSetName;
                    resolvedEntityType = resolved.Match.Value.EntityType.Name;
                    break;

                default:
                    return new DiagramJsonPayload
                    {
                        Success = false,
                        DbContext = dbContext.GetType().Name,
                        Format = "mermaid",
                        Error = "Could not resolve entity."
                    };
            }
        }

        try
        {
            return new DiagramJsonPayload
            {
                Success = true,
                DbContext = dbContext.GetType().Name,
                Format = "mermaid",
                DbSet = resolvedDbSet,
                EntityType = resolvedEntityType,
                Content = ErDiagramMermaidBuilder.Build(dbContext, entityName)
            };
        }
        catch (Exception failure)
        {
            return new DiagramJsonPayload
            {
                Success = false,
                DbContext = dbContext.GetType().Name,
                Format = "mermaid",
                DbSet = resolvedDbSet,
                EntityType = resolvedEntityType,
                Error = failure.Message
            };
        }
    }

    internal sealed class DiagramJsonPayload
    {
        public bool Success { get; init; }

        public string DbContext { get; init; } = string.Empty;

        public string Format { get; init; } = "mermaid";

        public string? DbSet { get; init; }

        public string? EntityType { get; init; }

        public string? Content { get; init; }

        public string? Error { get; init; }

        public string[]? KnownEntities { get; init; }
    }
}
