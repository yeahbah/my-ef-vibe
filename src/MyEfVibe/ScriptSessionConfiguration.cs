using System.Collections.Immutable;

namespace MyEfVibe;

internal sealed class ScriptSessionConfiguration
{
    internal static ScriptSessionConfiguration Empty { get; } = new();

    internal IReadOnlyList<string> SearchPaths { get; init; } = [];

    internal IReadOnlyList<string> LoadPaths { get; init; } = [];

    internal IReadOnlyList<string> AdditionalUsings { get; init; } = [];

    internal string? BasePath { get; init; }

    internal static ScriptSessionConfiguration FromCli(
        IEnumerable<string>? searchPaths,
        IEnumerable<string>? loadPaths,
        IEnumerable<string>? additionalUsings,
        string? basePath)
    {
        return new ScriptSessionConfiguration
        {
            SearchPaths = NormalizeList(searchPaths),
            LoadPaths = NormalizeList(loadPaths),
            AdditionalUsings = NormalizeUsings(additionalUsings),
            BasePath = string.IsNullOrWhiteSpace(basePath) ? null : basePath.Trim()
        };
    }

    internal ImmutableArray<string> ResolveSearchPaths(string fallbackBasePath)
    {
        var resolved = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scriptResolutionBase = ResolveScriptResolutionBase(fallbackBasePath);

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var fullPath = Path.GetFullPath(path.Trim());

            if (seen.Add(fullPath))
            {
                resolved.Add(fullPath);
            }
        }

        foreach (var path in SearchPaths)
        {
            Add(ScriptPathResolver.ResolvePath(path, scriptResolutionBase));
        }

        Add(BasePath);
        Add(fallbackBasePath);

        return [..resolved];
    }

    internal string ResolveScriptResolutionBase(string fallbackBasePath)
    {
        if (!string.IsNullOrWhiteSpace(BasePath))
        {
            return Path.GetFullPath(BasePath.Trim());
        }

        return Path.GetFullPath(fallbackBasePath);
    }

    internal ImmutableArray<string> ResolveLoadPaths(string fallbackBasePath)
    {
        var searchPaths = ResolveSearchPaths(fallbackBasePath);
        var resolved = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var loadPath in LoadPaths)
        {
            if (string.IsNullOrWhiteSpace(loadPath))
            {
                continue;
            }

            var fullPath = ScriptPathResolver.ResolveExistingFile(loadPath, searchPaths, fallbackBasePath);

            if (fullPath is null)
            {
                throw new FileNotFoundException($"Script load file not found: {loadPath.Trim()}");
            }

            if (seen.Add(fullPath))
            {
                resolved.Add(fullPath);
            }
        }

        return [..resolved];
    }

    internal string ResolveBasePath(string fallbackBasePath)
    {
        if (!string.IsNullOrWhiteSpace(BasePath))
        {
            return Path.GetFullPath(BasePath.Trim());
        }

        var searchPaths = ResolveSearchPaths(fallbackBasePath);

        return searchPaths.Length > 0
            ? searchPaths[0]
            : Path.GetFullPath(fallbackBasePath);
    }

    private static IReadOnlyList<string> NormalizeList(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return [];
        }

        return values
            .SelectMany(SplitConfigValues)
            .Select(static value => value.Trim())
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizeUsings(IEnumerable<string>? values)
    {
        return NormalizeList(values)
            .Select(static value => value.EndsWith(';') ? value[..^1].Trim() : value)
            .Where(static value => value.Length > 0)
            .ToArray();
    }

    private static IEnumerable<string> SplitConfigValues(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        foreach (var segment in value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment.Length > 0)
            {
                yield return segment;
            }
        }
    }
}
