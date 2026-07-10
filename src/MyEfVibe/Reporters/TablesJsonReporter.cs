using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyEfVibe.Reporters;

internal static class TablesJsonReporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    internal static void Write(object dbContext)
    {
        var payload = Build(dbContext);
        Console.WriteLine(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    internal static TablesJsonPayload Build(object dbContext)
    {
        var dbSets = EntityDescriptor.EnumerateDbSetEntities(dbContext).ToArray();
        var entityTypeNames = dbSets.Select(static entry => entry.EntityType).ToHashSet();

        var tables = dbSets
            .Select(entry =>
            {
                var modelEntity = EntityDescriptor.TryFindModelEntity(dbContext, entry.EntityType);
                var members = EntityDescriptor.DescribeMembers(entry.EntityType, modelEntity, entityTypeNames)
                    .Select(static member => new TablesJsonMember
                    {
                        Name = member.Name,
                        Type = member.TypeDisplay,
                        Nullable = member.Nullable,
                        Notes = string.IsNullOrWhiteSpace(member.Notes) ? null : member.Notes
                    })
                    .ToArray();

                return new TablesJsonEntry
                {
                    DbSet = entry.DbSetName,
                    EntityType = entry.EntityType.Name,
                    EntityTypeFullName = entry.EntityType.FullName,
                    Members = members
                };
            })
            .ToArray();

        return new TablesJsonPayload
        {
            DbContext = dbContext.GetType().Name,
            Tables = tables
        };
    }

    internal sealed class TablesJsonPayload
    {
        public string DbContext { get; init; } = string.Empty;

        public TablesJsonEntry[] Tables { get; init; } = [];
    }

    internal sealed class TablesJsonEntry
    {
        public string DbSet { get; init; } = string.Empty;

        public string EntityType { get; init; } = string.Empty;

        public string? EntityTypeFullName { get; init; }

        public TablesJsonMember[] Members { get; init; } = [];
    }

    internal sealed class TablesJsonMember
    {
        public string Name { get; init; } = string.Empty;

        public string Type { get; init; } = string.Empty;

        public string Nullable { get; init; } = string.Empty;

        public string? Notes { get; init; }
    }
}