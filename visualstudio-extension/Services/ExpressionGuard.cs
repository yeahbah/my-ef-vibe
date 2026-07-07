using System;
using System.Text.RegularExpressions;

namespace MyEfVibe.VisualStudio.Services;

internal static class ExpressionGuard
{
    private static readonly Regex[] BlockedEfPatterns =
    [
        new(@"\bSaveChanges(?:Async)?\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b(?:Add|AddRange|Update|UpdateRange|Remove|RemoveRange|Attach|AttachRange)\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bExecuteDelete(?:Async)?\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bExecuteUpdate(?:Async)?\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bExecuteSql(?:Raw)?(?:Async)?\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bDatabase\s*\.\s*(?:ExecuteSql(?:Raw)?|EnsureDeleted|EnsureCreated|Migrate)\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bFromSqlRaw\s*<", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    private static readonly Regex BlockComments = new(@"/\*[\s\S]*?\*/", RegexOptions.Compiled);
    private static readonly Regex LineComments = new(@"//.*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex ScriptReferenceDirective = new(@"^\s*#(?:load|r)\b", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex BlockedSql = new(@"\b(?:DROP|DELETE|INSERT|UPDATE|TRUNCATE|ALTER|CREATE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SqlApi = new(@"\b(?:ExecuteSql|FromSqlRaw|SqlQuery)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static bool IsReadOnly(string expression, out string reason)
    {
        reason = string.Empty;
        var trimmed = expression.Trim();
        if (trimmed.Length == 0)
        {
            reason = "Expression is empty.";
            return false;
        }

        var stripped = StripComments(trimmed);
        if (ScriptReferenceDirective.IsMatch(stripped))
        {
            reason = "Read-only mode: #load and #r directives are not allowed from guarded UI execution paths.";
            return false;
        }

        foreach (var pattern in BlockedEfPatterns)
        {
            if (!pattern.IsMatch(stripped))
                continue;

            reason = "Read-only mode: SaveChanges, Add/Update/Remove, ExecuteSql, ExecuteDelete/Update, and schema changes are not allowed.";
            return false;
        }

        if (BlockedSql.IsMatch(stripped) && SqlApi.IsMatch(stripped))
        {
            reason = "Destructive SQL keywords are not allowed in ExecuteSql / FromSqlRaw expressions.";
            return false;
        }

        return true;
    }

    private static string StripComments(string expression)
    {
        return LineComments.Replace(BlockComments.Replace(expression, " "), " ");
    }
}
