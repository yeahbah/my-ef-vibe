namespace MyEfVibe;

internal static class DbContextQuerySiteFilter
{
    private static readonly string[] ContextMemberAliases =
    [
        "DbContext",
        "_dbContext",
        "dbContext",
        "_context",
        "applicationDbContext",
        "_applicationDbContext",
        "appDbContext",
        "_appDbContext",
    ];

    internal static bool BelongsToSelectedContext(
        string statement,
        DbContextScanScope? scope,
        string? containingTypeName,
        DbContextContainingTypeIndex? containingTypeIndex)
    {
        if (scope is null)
            return true;

        if (ReferencesOtherContextType(statement, scope))
            return false;

        if (ReferencesSelectedContextType(statement, scope))
            return true;

        if (!string.IsNullOrWhiteSpace(containingTypeName)
            && containingTypeIndex is not null
            && containingTypeIndex.TryGetBoundContextType(containingTypeName, out var boundType))
        {
            if (string.Equals(boundType, scope.SelectedContextTypeName, StringComparison.Ordinal))
                return UsesContextMemberAlias(statement);

            return false;
        }

        if (statement.Contains("db.", StringComparison.Ordinal))
            return true;

        return false;
    }

    private static bool ReferencesSelectedContextType(string statement, DbContextScanScope scope) =>
        statement.Contains($"{scope.SelectedContextTypeName}.", StringComparison.Ordinal)
        || statement.Contains($"<{scope.SelectedContextTypeName}>", StringComparison.Ordinal);

    private static bool ReferencesOtherContextType(string statement, DbContextScanScope scope)
    {
        foreach (var other in scope.OtherContextTypeNames)
        {
            if (statement.Contains($"{other}.", StringComparison.Ordinal)
                || statement.Contains($"<{other}>", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool UsesContextMemberAlias(string statement)
    {
        foreach (var alias in ContextMemberAliases)
        {
            if (statement.Contains($"{alias}.", StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
