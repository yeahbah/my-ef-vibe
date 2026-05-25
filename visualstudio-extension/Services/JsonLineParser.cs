using System;
using System.Text.Json;

namespace MyEfVibe.VisualStudio.Services;

internal static class JsonLineParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    internal static T? ParseFirstJsonLine<T>(string output)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var trimmed = line.Trim();

            if (!trimmed.StartsWith("{", StringComparison.Ordinal))
                continue;

            try
            {
                return JsonSerializer.Deserialize<T>(trimmed, Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return null;
    }
}
