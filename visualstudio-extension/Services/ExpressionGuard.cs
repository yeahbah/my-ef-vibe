using System;
using System.Text.RegularExpressions;

namespace MyEfVibe.VisualStudio.Services;

internal static class ExpressionGuard
{
    private static readonly string[] BlockedTokens =
    {
        "SaveChanges",
        "ExecuteSql",
        "ExecuteUpdate",
        "ExecuteDelete",
        ".Add(",
        ".AddRange(",
        ".Update(",
        ".UpdateRange(",
        ".Remove(",
        ".RemoveRange(",
        "Database.ExecuteSql",
    };

    private static readonly Regex ScriptDirectivePattern = new(
        @"^\s*#(?:load\b|r(?=\s|""))",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    internal static bool IsReadOnly(string expression, out string reason)
    {
        reason = string.Empty;
        var stripped = StripComments(expression);

        if (ScriptDirectivePattern.IsMatch(stripped))
        {
            reason = "Read-only mode: #load and #r directives are not allowed.";
            return false;
        }

        foreach (var token in BlockedTokens)
        {
            if (stripped.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            reason = $"Expression contains '{token}', which is blocked by the read-only guard.";
            return false;
        }

        return true;
    }

    private static string StripComments(string expression)
    {
        var withoutBlockComments = Regex.Replace(expression, @"/\*[\s\S]*?\*/", " ");
        return Regex.Replace(withoutBlockComments, @"//.*$", " ", RegexOptions.Multiline);
    }
}
