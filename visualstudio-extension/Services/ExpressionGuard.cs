using System;
using System.Text.RegularExpressions;

namespace MyEfVibe.VisualStudio.Services;

internal static class ExpressionGuard
{
    private const string OptionalTypeArgs = @"(?:\s*<[^>(]+>)?";

    private static readonly Regex[] BlockedEfPatterns =
    {
        new Regex(@"\bSaveChanges(?:Async)?" + OptionalTypeArgs + @"\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\b(?:Add|AddRange|Update|UpdateRange|Remove|RemoveRange|Attach|AttachRange)\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bExecuteDelete(?:Async)?" + OptionalTypeArgs + @"\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bExecuteUpdate(?:Async)?" + OptionalTypeArgs + @"\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bExecuteSql(?:Raw|Interpolated)?(?:Async)?" + OptionalTypeArgs + @"\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\b(?:EnsureDeleted|EnsureCreated|Migrate)(?:Async)?" + OptionalTypeArgs + @"\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bDatabase\s*\.\s*(?:ExecuteSql(?:Raw|Interpolated)?(?:Async)?|EnsureDeleted(?:Async)?|EnsureCreated(?:Async)?|Migrate(?:Async)?)\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bGetService\s*<[^>]*>\s*\(\s*\)\s*\.\s*Delete\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bFromSqlRaw\s*<", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    private static readonly Regex BlockComments = new Regex(@"/\*[\s\S]*?\*/", RegexOptions.Compiled);
    private static readonly Regex LineComments = new Regex(@"//.*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex ScriptReferenceDirective = new Regex(@"^\s*#(?:load|r)\b", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex BlockedSql = new Regex(@"\b(?:DROP|DELETE|INSERT|UPDATE|TRUNCATE|ALTER|CREATE|EXEC(?:UTE)?|CALL|MERGE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SqlApi = new Regex(@"\b(?:ExecuteSql|FromSql|SqlQuery)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
            reason = "Destructive SQL keywords are not allowed in ExecuteSql / FromSql / SqlQuery expressions.";
            return false;
        }

        return true;
    }

    private static string StripComments(string expression)
    {
        return LineComments.Replace(BlockComments.Replace(expression, " "), " ");
    }
}
