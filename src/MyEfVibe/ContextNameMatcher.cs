namespace MyEfVibe;

internal static class ContextNameMatcher
{
    internal static bool Matches(Type dbContextType, string contextName)
    {
        if (string.IsNullOrWhiteSpace(contextName))
        {
            return false;
        }

        var trimmed = contextName.Trim();

        if (string.Equals(dbContextType.Name, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(dbContextType.FullName, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (dbContextType.FullName is not null
            && dbContextType.FullName.EndsWith($".{trimmed}", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}