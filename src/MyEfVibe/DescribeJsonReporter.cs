using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyEfVibe;

internal static class DescribeJsonReporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    internal static void Write(object dbContext, string entityName)
    {
        var payload = Build(dbContext, entityName);
        Console.WriteLine(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    internal static DescribeJsonPayload Build(object dbContext, string entityName)
    {
        if (string.IsNullOrWhiteSpace(entityName))
        {
            return new DescribeJsonPayload
            {
                Success = false,
                Error = "Entity name is required."
            };
        }

        var dbSets = EntityDescriptor.EnumerateDbSetEntities(dbContext).ToArray();

        if (dbSets.Length == 0)
        {
            return new DescribeJsonPayload
            {
                Success = false,
                Error = "No DbSet properties found on this context."
            };
        }

        switch (EntityDescriptor.TryResolveEntity(dbSets, entityName.Trim(), out var resolved))
        {
            case EntityDescriptor.EntityResolveResult.NotFound:
                return new DescribeJsonPayload
                {
                    Success = false,
                    Error = $"Entity `{entityName}` was not found.",
                    KnownEntities = dbSets
                        .Select(static entry => $"{entry.DbSetName} · {entry.EntityType.Name}")
                        .ToArray()
                };

            case EntityDescriptor.EntityResolveResult.Ambiguous:
                return new DescribeJsonPayload
                {
                    Success = false,
                    Error = $"Multiple entities match `{entityName}`. Use a DbSet name or full type name.",
                    KnownEntities = resolved.AmbiguousMatches!
                        .Select(static entry => $"{entry.DbSetName} · {entry.EntityType.Name}")
                        .ToArray()
                };

            case EntityDescriptor.EntityResolveResult.Found:
                break;

            default:
                return new DescribeJsonPayload { Success = false, Error = "Could not resolve entity." };
        }

        var match = resolved.Match!.Value;
        var entityType = match.EntityType;
        var modelEntity = EntityDescriptor.TryFindModelEntity(dbContext, entityType);
        var entityTypeNames = dbSets.Select(static entry => entry.EntityType).ToHashSet();
        var members = EntityDescriptor.DescribeMembers(entityType, modelEntity, entityTypeNames)
            .Select(static member => new DescribeJsonMember
            {
                Name = member.Name,
                Type = member.TypeDisplay,
                Nullable = member.Nullable,
                Notes = string.IsNullOrWhiteSpace(member.Notes) ? null : member.Notes
            })
            .ToArray();

        return new DescribeJsonPayload
        {
            Success = true,
            DbSet = match.DbSetName,
            EntityType = entityType.Name,
            EntityTypeFullName = entityType.FullName,
            Members = members
        };
    }

    internal sealed class DescribeJsonPayload
    {
        public bool Success { get; init; }

        public string? DbSet { get; init; }

        public string? EntityType { get; init; }

        public string? EntityTypeFullName { get; init; }

        public DescribeJsonMember[]? Members { get; init; }

        public string? Error { get; init; }

        public string[]? KnownEntities { get; init; }
    }

    internal sealed class DescribeJsonMember
    {
        public string Name { get; init; } = string.Empty;

        public string Type { get; init; } = string.Empty;

        public string Nullable { get; init; } = string.Empty;

        public string? Notes { get; init; }
    }
}