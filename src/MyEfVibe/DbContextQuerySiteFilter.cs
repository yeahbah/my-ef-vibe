namespace MyEfVibe;

internal static class DbContextQuerySiteFilter
{
    internal static bool BelongsToSelectedContext(
        string statement,
        DbContextScanScope? scope,
        string? containingTypeName,
        DbContextContainingTypeIndex? containingTypeIndex,
        DbContextInstanceIdentifierIndex? instanceIndex = null)
    {
        if (scope is null)
            return true;

        if (instanceIndex?.StatementReferencesOtherContextInstance(statement) == true)
            return false;

        if (ReferencesOtherContextType(statement, scope))
            return false;

        if (ReferencesSelectedContextType(statement, scope))
            return true;

        if (instanceIndex?.StatementReferencesSelectedContextInstance(statement) == true)
            return true;

        if (UsesBuiltInContextMemberAlias(statement))
        {
            if (TryGetBoundContextType(containingTypeName, containingTypeIndex, out var boundType)
                && !scope.IsSelectedContextType(boundType))
                return false;

            return true;
        }

        if (statement.Contains("db.", StringComparison.Ordinal))
        {
            if (TryGetBoundContextType(containingTypeName, containingTypeIndex, out var boundType)
                && !scope.IsSelectedContextType(boundType))
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

    private static bool ReferencesSelectedContextType(string statement, DbContextScanScope scope)
    {
        foreach (var selected in scope.SelectedContextTypeNames)
        {
            if (statement.Contains($"{selected}.", StringComparison.Ordinal)
                || statement.Contains($"<{selected}>", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

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

    private static bool UsesBuiltInContextMemberAlias(string statement)
    {
        foreach (var prefix in DbContextQueryMarkers.BuiltInMemberPrefixes)
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
