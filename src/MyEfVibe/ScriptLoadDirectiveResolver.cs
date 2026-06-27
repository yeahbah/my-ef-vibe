using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace MyEfVibe;

internal static partial class ScriptLoadDirectiveResolver
{
    [GeneratedRegex("^#load\\s+\"(?<path>(?:\\\\.|[^\"])*)\"\\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LoadDirectivePattern();

    internal static string? TryParseLoadPath(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var match = LoadDirectivePattern().Match(line.Trim());

        if (!match.Success)
        {
            return null;
        }

        return match.Groups["path"].Value.Replace("\\\"", "\"", StringComparison.Ordinal);
    }

    internal static string ResolveLoadPath(
        string loadPath,
        ImmutableArray<string> searchPaths,
        string basePath)
    {
        return ScriptPathResolver.ResolveExistingFile(loadPath, searchPaths, basePath)
               ?? throw new FileNotFoundException($"Script load file not found: {loadPath}");
    }

    internal static string FormatLoadDirective(string loadPath)
    {
        return $"#load \"{ScriptPathResolver.EscapeForLoadDirective(loadPath)}\"";
    }

    internal static string ResolveDirectives(
        string directives,
        ImmutableArray<string> searchPaths,
        string basePath)
    {
        if (string.IsNullOrWhiteSpace(directives))
        {
            return directives;
        }

        var lines = InputLineUtilities.SplitLines(directives);
        var resolved = new string[lines.Length];

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];

            if (string.IsNullOrWhiteSpace(line))
            {
                resolved[index] = line;
                continue;
            }

            var match = LoadDirectivePattern().Match(line.Trim());

            if (!match.Success)
            {
                resolved[index] = line;
                continue;
            }

            var loadPath = match.Groups["path"].Value.Replace("\\\"", "\"", StringComparison.Ordinal);
            var fullPath = ResolveLoadPath(loadPath, searchPaths, basePath);

            resolved[index] = FormatLoadDirective(fullPath);
        }

        return InputLineUtilities.JoinLines(resolved);
    }
}
