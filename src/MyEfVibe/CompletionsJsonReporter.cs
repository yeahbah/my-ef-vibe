using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyEfVibe;

internal static class CompletionsService
{
    private static readonly string[] DbQueryableMembers =
    [
        "AsNoTracking",
        "AsNoTrackingWithIdentityResolution",
        "AsSplitQuery",
        "AsSingleQuery",
        "Count",
        "CountAsync",
        "Any",
        "AnyAsync",
        "First",
        "FirstAsync",
        "FirstOrDefault",
        "FirstOrDefaultAsync",
        "Single",
        "SingleAsync",
        "SingleOrDefault",
        "SingleOrDefaultAsync",
        "Where",
        "Select",
        "Include",
        "ThenInclude",
        "OrderBy",
        "OrderByDescending",
        "Take",
        "Skip",
        "ToList",
        "ToListAsync",
        "ToArray",
        "ToArrayAsync",
    ];

    internal static IReadOnlyList<CompletionJsonItem> GetCompletions(object dbContext, string prefix)
    {
        prefix = prefix?.Trim() ?? string.Empty;

        if (prefix.Length == 0 || string.Equals(prefix, "db", StringComparison.OrdinalIgnoreCase))
        {
            return SchemaBrowser.GetDbSets(dbContext)
                .Select(entry => new CompletionJsonItem
                {
                    Label = entry.DbSet,
                    InsertText = entry.DbSet,
                    Kind = "property",
                    Detail = entry.EntityType,
                })
                .ToArray();
        }

        if (!prefix.StartsWith("db.", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<CompletionJsonItem>();

        var remainder = prefix[3..];
        var dotIndex = remainder.IndexOf('.');

        if (dotIndex < 0)
        {
            return SchemaBrowser.GetDbSets(dbContext)
                .Where(entry => entry.DbSet.StartsWith(remainder, StringComparison.OrdinalIgnoreCase))
                .Select(entry => new CompletionJsonItem
                {
                    Label = entry.DbSet,
                    InsertText = entry.DbSet,
                    Kind = "property",
                    Detail = entry.EntityType,
                })
                .ToArray();
        }

        var memberPrefix = remainder[(dotIndex + 1)..];

        return DbQueryableMembers
            .Where(member => member.StartsWith(memberPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(member => new CompletionJsonItem
            {
                Label = member,
                InsertText = member,
                Kind = "method",
            })
            .ToArray();
    }
}

internal static class CompletionsJsonReporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    internal static void Write(object dbContext, string prefix)
    {
        var payload = new CompletionsJsonPayload
        {
            Prefix = prefix,
            Items = CompletionsService.GetCompletions(dbContext, prefix),
        };

        Console.WriteLine(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    internal sealed class CompletionsJsonPayload
    {
        public string Prefix { get; init; } = string.Empty;

        public IReadOnlyList<CompletionJsonItem> Items { get; init; } = Array.Empty<CompletionJsonItem>();
    }
}

internal sealed class CompletionJsonItem
{
    public string Label { get; init; } = string.Empty;

    public string InsertText { get; init; } = string.Empty;

    public string Kind { get; init; } = "property";

    public string? Detail { get; init; }
}
