using System.Text.Json;
using System.Text.Json.Serialization;
using MyEfVibe.Linq;

namespace MyEfVibe.Reporters;

internal static class TranslateJsonReporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    internal static void Write(LinqSqlTranslationResult result)
    {
        var payload = new TranslateJsonPayload
        {
            Success = !string.IsNullOrWhiteSpace(result.Sql),
            Sql = result.Sql,
            Note = result.Note
        };

        Console.WriteLine(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    private sealed class TranslateJsonPayload
    {
        public bool Success { get; init; }

        public string? Sql { get; init; }

        public string? Note { get; init; }
    }
}
