using System.Collections;
using System.Reflection;

namespace MyEfVibe;

internal static class DbContextModelEntityDiscovery
{
    private const string IModelFullName = "Microsoft.EntityFrameworkCore.Metadata.IModel";
    private const string IEntityTypeFullName = "Microsoft.EntityFrameworkCore.Metadata.IEntityType";

    internal static IReadOnlySet<string> DiscoverIncludedEntityTypeNames(object dbContext)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var model = TryGetModel(dbContext);

            if (model is null)
                return names;

            var getEntityTypes = model.GetType()
                .GetInterfaces()
                .FirstOrDefault(iface => string.Equals(iface.FullName, IModelFullName, StringComparison.Ordinal))
                ?.GetMethod(
                    "GetEntityTypes",
                    BindingFlags.Public | BindingFlags.Instance,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);

            if (getEntityTypes?.Invoke(model, null) is not IEnumerable entityTypes)
                return names;

            foreach (var entityType in entityTypes)
            {
                if (entityType is null)
                    continue;

                var clrType = entityType.GetType()
                        .GetProperty("ClrType", BindingFlags.Public | BindingFlags.Instance)
                        ?.GetValue(entityType) as Type
                    ?? entityType.GetType()
                        .GetInterfaces()
                        .FirstOrDefault(iface => string.Equals(iface.FullName, IEntityTypeFullName, StringComparison.Ordinal))
                        ?.GetProperty("ClrType", BindingFlags.Public | BindingFlags.Instance)
                        ?.GetValue(entityType) as Type;

                if (!string.IsNullOrWhiteSpace(clrType?.Name))
                    names.Add(clrType.Name);
            }
        }
        catch (TargetInvocationException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return names;
    }

    private static object? TryGetModel(object dbContext)
    {
        for (var type = dbContext.GetType(); type is not null; type = type.BaseType)
        {
            var modelProperty = type.GetProperty(
                "Model",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            if (modelProperty is not null)
                return modelProperty.GetValue(dbContext);
        }

        return null;
    }
}
