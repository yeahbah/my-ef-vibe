using System.Reflection;

namespace MyEfVibe;

internal static class DbSetEntityDiscovery
{
    internal static IReadOnlyList<string> DiscoverEntityTypeNames(object dbContext)
    {
        var names = new List<string>();

        foreach (var property in dbContext.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.PropertyType.IsGenericType)
                continue;

            if (!typeof(System.Linq.IQueryable).IsAssignableFrom(property.PropertyType))
                continue;

            var elementType = property.PropertyType.GetGenericArguments()[0];

            if (!string.IsNullOrWhiteSpace(elementType.Name))
                names.Add(elementType.Name);
        }

        return names
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
    }

    internal static string? SelectRepresentativeEntityName(IReadOnlyList<string> entityTypeNames)
    {
        if (entityTypeNames.Count == 0)
            return null;

        foreach (var preferred in new[] { "Product", "Person", "Customer", "Order" })
        {
            if (entityTypeNames.Contains(preferred, StringComparer.Ordinal))
                return preferred;
        }

        return entityTypeNames[0];
    }
}
