using System;

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

    internal static bool IsReadOnly(string expression, out string reason)
    {
        reason = string.Empty;

        foreach (var token in BlockedTokens)
        {
            if (expression.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            reason = $"Expression contains '{token}', which is blocked by the read-only guard.";
            return false;
        }

        return true;
    }
}
