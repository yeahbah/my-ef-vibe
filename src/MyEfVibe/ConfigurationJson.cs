using System.Text.Json;

namespace MyEfVibe;

internal static class ConfigurationJson
{
    internal static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    internal static bool TryParseFile(string path, out JsonDocument? document)
    {
        document = null;

        string json;

        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            document = JsonDocument.Parse(json, DocumentOptions);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}