using System.Collections.Immutable;
using System.Text;

namespace MyEfVibe;

internal static class ScriptPathResolver
{
    internal static string? ResolveExistingFile(
        string path,
        ImmutableArray<string> searchPaths,
        string fallbackBasePath)
    {
        var trimmed = path.Trim();

        if (Path.IsPathRooted(trimmed))
        {
            return File.Exists(trimmed) ? Path.GetFullPath(trimmed) : null;
        }

        foreach (var searchPath in searchPaths)
        {
            var candidate = Path.GetFullPath(Path.Combine(searchPath, trimmed));

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var relativeToFallback = ResolvePath(trimmed, fallbackBasePath);

        return File.Exists(relativeToFallback) ? relativeToFallback : null;
    }

    internal static string ResolvePath(string path, string basePath)
    {
        var trimmed = path.Trim();

        if (Path.IsPathRooted(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        return Path.GetFullPath(Path.Combine(basePath, trimmed));
    }

    internal static string EscapeForLoadDirective(string path)
    {
        return path
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    internal static string BuildLoadBootstrap(ImmutableArray<string> loadPaths)
    {
        if (loadPaths.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var loadPath in loadPaths)
        {
            builder.Append("#load \"");
            builder.Append(EscapeForLoadDirective(loadPath));
            builder.AppendLine("\"");
        }

        return builder.ToString().TrimEnd();
    }
}
