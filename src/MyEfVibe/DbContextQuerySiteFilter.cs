namespace MyEfVibe;

internal static class DbContextQuerySiteFilter
{
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

        if (UsesContextMemberAlias(statement))
        {
            if (TryGetBoundContextType(containingTypeName, containingTypeIndex, out var boundType)
                && !string.Equals(boundType, scope.SelectedContextTypeName, StringComparison.Ordinal))
                return false;

            return true;
        }

        if (statement.Contains("db.", StringComparison.Ordinal))
        {
            if (TryGetBoundContextType(containingTypeName, containingTypeIndex, out var boundType)
                && !string.Equals(boundType, scope.SelectedContextTypeName, StringComparison.Ordinal))
                return false;

            return true;
        }

        return false;
    }

    private static bool TryGetBoundContextType(
        string? containingTypeName,
        DbContextContainingTypeIndex? containingTypeIndex,
        out string boundType)
    {
        boundType = string.Empty;

        if (string.IsNullOrWhiteSpace(containingTypeName) || containingTypeIndex is null)
            return false;

        return containingTypeIndex.TryGetBoundContextType(containingTypeName, out boundType!);
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
        foreach (var prefix in DbContextQueryMarkers.MemberPrefixes)
        {
            if (prefix.Equals("db.", StringComparison.Ordinal))
                continue;

            var alias = prefix[..^1];

            if (statement.Contains($"{alias}.", StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
