using System.Reflection;

namespace MyEfVibe;

/// <summary>
///     Resolves properties on entities referenced by outer-scope variables (for example <c>note.UserId</c>).
/// </summary>
internal static class OuterEntityMemberResolver
{
    internal static bool TryResolvePropertyType(
        Type dbContextType,
        string variableName,
        string memberName,
        out Type? propertyType)
    {
        propertyType = null;

        var candidates = new List<(Type EntityType, Type PropertyType)>();

        foreach (var entityType in DbSetEntityDiscovery.DiscoverEntityClrTypes(dbContextType))
        {
            var property = entityType.GetProperty(
                memberName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (property is null)
            {
                continue;
            }

            candidates.Add((entityType, property.PropertyType));
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        var namedMatches = candidates
            .Where(entry => VariableNameMatchesEntity(variableName, entry.EntityType))
            .ToArray();

        var selected = namedMatches switch
        {
            [var single] => single,
            [] when candidates.Count == 1 => candidates[0],
            _ => default
        };

        if (selected == default)
        {
            return false;
        }

        propertyType = selected.PropertyType;

        return true;
    }

    private static bool VariableNameMatchesEntity(string variableName, Type entityType)
    {
        if (string.IsNullOrWhiteSpace(variableName))
        {
            return false;
        }

        var entityName = entityType.Name;

        if (string.Equals(variableName, entityName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (variableName.Length >= 3
            && entityName.EndsWith(variableName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (entityName.Length > variableName.Length
            && entityName.StartsWith(variableName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}