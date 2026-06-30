using System.Text.Json;
using System.Text.Json.Serialization;
using MyEfVibe.Workspace;

namespace MyEfVibe;

internal static class ServeProtocol
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    internal static ServeRequest? TryParseRequest(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ServeRequest>(line, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    internal static void WriteReady(WorkspaceRuntime runtime)
    {
        var payload = new ServeReadyResponse
        {
            Type = "ready",
            DbContext = runtime.DbContextName,
            WorkspaceRoot = runtime.WorkspaceRoot,
            SessionDirectory = runtime.SessionDirectory
        };

        WriteLine(payload);
    }

    internal static void WriteError(string message)
    {
        WriteLine(new ServeErrorResponse { Type = "error", Message = message });
    }

    internal static void WritePong()
    {
        WriteLine(new ServePongResponse { Type = "pong" });
    }

    private static void WriteLine<T>(T payload)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    internal sealed class ServeRequest
    {
        public string? Type { get; init; }

        public string? Expression { get; init; }

        public bool WithPlan { get; init; }

        public string? Entity { get; init; }

        public string? Mode { get; init; }

        public bool RespectDismissals { get; init; }

        public string? MinSeverity { get; init; }

        public string? Prefix { get; init; }

        public string? Sql { get; init; }

        public JsonElement? Updates { get; init; }

        public JsonElement? Deletes { get; init; }

        public int? Skip { get; init; }

        public int? PageSize { get; init; }
    }

    private sealed class ServeReadyResponse
    {
        public string Type { get; init; } = "ready";

        public string? DbContext { get; init; }

        public string? WorkspaceRoot { get; init; }

        public string? SessionDirectory { get; init; }
    }

    private sealed class ServeErrorResponse
    {
        public string Type { get; init; } = "error";

        public string? Message { get; init; }
    }

    private sealed class ServePongResponse
    {
        public string Type { get; init; } = "pong";
    }
}