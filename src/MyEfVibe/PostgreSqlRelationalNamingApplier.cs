using System.Reflection;

namespace MyEfVibe;

/// <summary>
/// Lowercases schema, table, and column names for PostgreSQL (matches AdventureWorks sample
/// <c>PostgreSqlRelationalNaming</c>). Uses reflection so myefvibe does not compile against EF Core.
/// </summary>
public static class PostgreSqlRelationalNamingApplier
{
    private const string IModelFullName = "Microsoft.EntityFrameworkCore.Metadata.IModel";
    private const string IEntityTypeFullName = "Microsoft.EntityFrameworkCore.Metadata.IEntityType";
    private const string IPropertyFullName = "Microsoft.EntityFrameworkCore.Metadata.IProperty";

    public static void CustomizeAfterBase(object modelBuilder, object context)
    {
        if (!UsesNpgsqlProvider(context))
            return;

        Apply(modelBuilder);
    }

    internal static void Apply(object modelBuilder)
    {
        var model = modelBuilder.GetType()
            .GetProperty("Model", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(modelBuilder);

        if (model is null)
            return;

        var getEntityTypes = model.GetType()
            .GetInterfaces()
            .FirstOrDefault(iface => string.Equals(iface.FullName, IModelFullName, StringComparison.Ordinal))
            ?.GetMethod(
                "GetEntityTypes",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

        if (getEntityTypes?.Invoke(model, null) is not System.Collections.IEnumerable entityTypes)
            return;

        foreach (var entityType in entityTypes)
        {
            if (entityType is null)
                continue;

            var schema = entityType.GetType()
                .GetInterfaces()
                .FirstOrDefault(iface => string.Equals(iface.FullName, IEntityTypeFullName, StringComparison.Ordinal))
                ?.GetMethod("GetSchema", BindingFlags.Public | BindingFlags.Instance)
                ?.Invoke(entityType, null) as string;

            if (!string.IsNullOrEmpty(schema))
            {
                entityType.GetType()
                    .GetInterfaces()
                    .First(iface => string.Equals(iface.FullName, IEntityTypeFullName, StringComparison.Ordinal))
                    .GetMethod("SetSchema", BindingFlags.Public | BindingFlags.Instance)!
                    .Invoke(entityType, [schema.ToLowerInvariant()]);
            }

            var tableName = entityType.GetType()
                .GetInterfaces()
                .FirstOrDefault(iface => string.Equals(iface.FullName, IEntityTypeFullName, StringComparison.Ordinal))
                ?.GetMethod("GetTableName", BindingFlags.Public | BindingFlags.Instance)
                ?.Invoke(entityType, null) as string;

            if (!string.IsNullOrEmpty(tableName))
            {
                entityType.GetType()
                    .GetInterfaces()
                    .First(iface => string.Equals(iface.FullName, IEntityTypeFullName, StringComparison.Ordinal))
                    .GetMethod("SetTableName", BindingFlags.Public | BindingFlags.Instance)!
                    .Invoke(entityType, [tableName.ToLowerInvariant()]);
            }

            var getProperties = entityType.GetType()
                .GetInterfaces()
                .FirstOrDefault(iface => string.Equals(iface.FullName, IEntityTypeFullName, StringComparison.Ordinal))
                ?.GetMethod("GetProperties", BindingFlags.Public | BindingFlags.Instance);

            if (getProperties?.Invoke(entityType, null) is not System.Collections.IEnumerable properties)
                continue;

            foreach (var property in properties)
            {
                if (property is null)
                    continue;

                var columnName = property.GetType()
                    .GetInterfaces()
                    .FirstOrDefault(iface => string.Equals(iface.FullName, IPropertyFullName, StringComparison.Ordinal))
                    ?.GetMethod("GetColumnName", BindingFlags.Public | BindingFlags.Instance)
                    ?.Invoke(property, null) as string;

                if (string.IsNullOrEmpty(columnName))
                    continue;

                property.GetType()
                    .GetInterfaces()
                    .First(iface => string.Equals(iface.FullName, IPropertyFullName, StringComparison.Ordinal))
                    .GetMethod("SetColumnName", BindingFlags.Public | BindingFlags.Instance)!
                    .Invoke(property, [columnName.ToLowerInvariant()]);
            }
        }
    }

    private static bool UsesNpgsqlProvider(object context)
    {
        var database = context.GetType()
            .GetProperty("Database", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(context);

        var providerName = database?.GetType()
            .GetProperty("ProviderName", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(database) as string;

        return providerName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
    }
}
