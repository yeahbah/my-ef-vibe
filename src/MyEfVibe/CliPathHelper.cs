namespace MyEfVibe;

internal static class CliPathHelper
{
    internal static DirectoryInfo ResolveWorkspace(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? new DirectoryInfo(SessionPaths.GetDefaultWorkspaceDirectory())
            : new DirectoryInfo(path);
    }

    internal static FileInfo? ToFileInfo(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : new FileInfo(path);
    }

    internal static string? ResolveOneShotExpression(string? expressionOption, IEnumerable<string>? expressionParts)
    {
        if (!string.IsNullOrWhiteSpace(expressionOption))
        {
            return expressionOption.Trim();
        }

        if (expressionParts is null)
        {
            return null;
        }

        var tokens = expressionParts
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return tokens.Length == 0 ? null : string.Join(' ', tokens).Trim();
    }
}