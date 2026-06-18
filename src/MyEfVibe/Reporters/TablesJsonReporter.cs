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
        var tables = SchemaBrowser.GetDbSets(dbContext)
            .Select(entry => new TablesJsonEntry
            {
                DbSet = entry.DbSet,
                EntityType = entry.EntityType,
                EntityTypeFullName = entry.EntityTypeFullName
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
    }
}