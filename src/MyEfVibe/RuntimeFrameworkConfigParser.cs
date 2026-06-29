using System.Text.RegularExpressions;

namespace MyEfVibe;

/// <summary>
///     Parses <c>*.runtimeconfig.json</c> without <c>System.Text.Json</c> so shared-framework indexing
///     can run before workspace <c>System.Text.Json</c> bootstrap.
/// </summary>
internal static partial class RuntimeFrameworkConfigParser
{
    internal readonly record struct FrameworkReference(string Name, string Version);

    internal static IEnumerable<FrameworkReference> ReadFrameworks(string runtimeConfigPath)
    {
        if (!File.Exists(runtimeConfigPath))
        {
            yield break;
        }

        string json;

        try
        {
            json = File.ReadAllText(runtimeConfigPath);
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var frameworkObject in ExtractFrameworkObjects(json))
        {
            var name = ReadStringProperty(frameworkObject, "name");
            var version = ReadStringProperty(frameworkObject, "version");

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(version))
            {
                yield return new FrameworkReference(name, version);
            }
        }
    }

    private static IEnumerable<string> ExtractFrameworkObjects(string json)
    {
        var frameworksIndex = json.IndexOf("\"frameworks\"", StringComparison.Ordinal);

        if (frameworksIndex < 0)
        {
            yield break;
        }

        var arrayStart = json.IndexOf('[', frameworksIndex);

        if (arrayStart < 0)
        {
            yield break;
        }

        var depth = 0;
        var objectStart = -1;

        for (var index = arrayStart; index < json.Length; index++)
        {
            switch (json[index])
            {
                case '[':
                    depth++;
                    break;
                case '{':
                    if (depth == 1)
                    {
                        objectStart = index;
                    }

                    depth++;
                    break;
                case '}':
                    if (depth == 2 && objectStart >= 0)
                    {
                        yield return json[objectStart..(index + 1)];
                        objectStart = -1;
                    }

                    depth--;
                    break;
                case ']':
                    depth--;

                    if (depth == 0)
                    {
                        yield break;
                    }

                    break;
            }
        }
    }

    private static string? ReadStringProperty(string objectJson, string propertyName)
    {
        foreach (Match match in StringPropertyRegex().Matches(objectJson))
        {
            if (string.Equals(match.Groups["name"].Value, propertyName, StringComparison.Ordinal))
            {
                return match.Groups["value"].Value;
            }
        }

        return null;
    }

    [GeneratedRegex(
        "\"(?<name>[^\"]+)\"\\s*:\\s*\"(?<value>[^\"]*)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex StringPropertyRegex();
}
