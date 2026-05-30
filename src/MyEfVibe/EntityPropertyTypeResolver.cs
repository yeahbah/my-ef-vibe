using System.Reflection;

namespace MyEfVibe;

internal static class EntityPropertyTypeResolver
{
    internal static bool TryGetPropertyType(
        Type dbContextType,
        string entityTypeName,
        string propertyName,
        out Type? propertyType)
    {
        propertyType = null;

        if (!TryResolveEntityType(dbContextType, entityTypeName, out var entityType))
        {
            return false;
        }

        var property = entityType.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null)
        {
            return false;
        }

        propertyType = property.PropertyType;

        return true;
    }

    private static bool TryResolveEntityType(Type dbContextType, string entityTypeName, out Type entityType)
    {
        entityType = null!;

        var resolved = dbContextType.Assembly.GetType(entityTypeName, false)
                       ?? Type.GetType(entityTypeName, false);

        if (resolved is not null)
        {
            entityType = resolved;

            return true;
        }

        foreach (var reference in dbContextType.Assembly.GetReferencedAssemblies())
        {
            try
            {
                var assembly = Assembly.Load(reference);

                resolved = assembly.GetType(entityTypeName, false);

                if (resolved is null)
                {
                    continue;
                }

                entityType = resolved;

                return true;
            }
            catch (FileNotFoundException)
            {
                // Assembly not available in the scan host.
            }
        }

        return false;
    }
}