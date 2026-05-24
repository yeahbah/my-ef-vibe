using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyEfVibe;

/// <summary>
/// Minimal JSON-RPC language server for <c>db.</c> completion in C# editors.
/// Supports initialize, textDocument/completion, shutdown, and exit.
/// </summary>
internal static class LanguageServerRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static async Task<int> RunAsync(
        object dbContext,
        CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await ReadMessageAsync(Console.OpenStandardInput(), cancellationToken);

            if (message is null)
                break;

            if (!TryGetMethod(message.Value, out var method, out var id))
                continue;

            switch (method)
            {
                case "initialize":
                    WriteResponse(id, new
                    {
                        capabilities = new
                        {
                            completionProvider = new
                            {
                                triggerCharacters = new[] { "." },
                            },
                        },
                        serverInfo = new
                        {
                            name = "efvibe",
                            version = ToolInfo.GetVersion(),
                        },
                    });
                    break;

                case "initialized":
                    break;

                case "textDocument/completion":
                    WriteResponse(id, BuildCompletionResponse(dbContext, message.Value));
                    break;

                case "shutdown":
                    WriteResponse(id, null);
                    break;

                case "exit":
                    return 0;

                default:
                    if (id is not null)
                        WriteResponse(id, new { });
                    break;
            }
        }

        return 0;
    }

    private static object BuildCompletionResponse(object dbContext, JsonElement message)
    {
        var prefix = TryReadPrefix(message) ?? "db.";
        var items = CompletionsService.GetCompletions(dbContext, prefix);

        return new
        {
            isIncomplete = false,
            items = items.Select(item => new
            {
                label = item.Label,
                kind = item.Kind switch
                {
                    "method" => 2,
                    _ => 10,
                },
                detail = item.Detail,
                insertText = item.InsertText,
            }).ToArray(),
        };
    }

    private static string? TryReadPrefix(JsonElement message)
    {
        if (!message.TryGetProperty("params", out var parameters))
            return null;

        if (!parameters.TryGetProperty("textDocument", out var textDocument))
            return "db.";

        if (!textDocument.TryGetProperty("text", out var textElement))
            return "db.";

        var text = textElement.GetString() ?? string.Empty;

        if (!parameters.TryGetProperty("position", out var position)
            || !position.TryGetProperty("character", out var characterElement))
        {
            return text.Trim().Length == 0 ? "db." : text;
        }

        var character = characterElement.GetInt32();
        var lineEnd = text.LastIndexOf('\n');

        if (lineEnd < 0)
            lineEnd = -1;

        var line = text[(lineEnd + 1)..];
        var safeCharacter = Math.Clamp(character, 0, line.Length);

        return line[..safeCharacter];
    }

    private static bool TryGetMethod(JsonElement message, out string method, out object? id)
    {
        method = string.Empty;
        id = null;

        if (!message.TryGetProperty("method", out var methodElement))
            return false;

        method = methodElement.GetString() ?? string.Empty;

        if (message.TryGetProperty("id", out var idElement))
        {
            id = idElement.ValueKind switch
            {
                JsonValueKind.Number => idElement.GetInt32(),
                JsonValueKind.String => idElement.GetString(),
                _ => null,
            };
        }

        return true;
    }

    private static void WriteResponse(object? id, object? result)
    {
        var payload = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
        };

        if (result is not null)
            payload["result"] = result;

        WriteMessage(payload);
    }

    private static void WriteMessage(object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        Console.Out.Write($"Content-Length: {bytes.Length}\r\n\r\n");
        Console.Out.Write(json);
        Console.Out.Flush();
    }

    private static async Task<JsonElement?> ReadMessageAsync(Stream input, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var headerLine = await ReadHeaderLineAsync(input, cancellationToken);

            if (headerLine is null)
                return null;

            if (headerLine.Length == 0)
                break;

            var separator = headerLine.IndexOf(':');

            if (separator <= 0)
                continue;

            headers[headerLine[..separator].Trim()] = headerLine[(separator + 1)..].Trim();
        }

        if (!headers.TryGetValue("Content-Length", out var lengthRaw)
            || !int.TryParse(lengthRaw, out var length)
            || length <= 0)
        {
            return null;
        }

        var buffer = new byte[length];
        var read = 0;

        while (read < length)
        {
            var chunk = await input.ReadAsync(buffer.AsMemory(read, length - read), cancellationToken);

            if (chunk == 0)
                return null;

            read += chunk;
        }

        using var document = JsonDocument.Parse(buffer);

        return document.RootElement.Clone();
    }

    private static async Task<string?> ReadHeaderLineAsync(Stream input, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();

        while (true)
        {
            var value = input.ReadByte();

            if (value < 0)
                return builder.Length == 0 ? null : builder.ToString();

            if (value == '\n')
            {
                if (builder.Length > 0 && builder[^1] == '\r')
                    builder.Length--;

                return builder.ToString();
            }

            builder.Append((char)value);
        }
    }
}
