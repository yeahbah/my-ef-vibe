using System.Collections;
using System.Reflection;

namespace MyEfVibe;

/// <summary>
///     Resolves EF Core relational metadata extension methods and enumerates model entities via reflection.
/// </summary>
internal static class RelationalMetadataReflection
{
    private const string IModelFullName = "Microsoft.EntityFrameworkCore.Metadata.IModel";
    private const string IEntityTypeFullName = "Microsoft.EntityFrameworkCore.Metadata.IEntityType";
    private const string RelationalAssemblyName = "Microsoft.EntityFrameworkCore.Relational";
    private const string EntityTypeExtensionsTypeName = "Microsoft.EntityFrameworkCore.RelationalEntityTypeExtensions";
    private const string PropertyExtensionsTypeName = "Microsoft.EntityFrameworkCore.RelationalPropertyExtensions";

    private static readonly Dictionary<string, RelationalMetadataMethods?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    internal static RelationalMetadataMethods? Resolve(object anchor)
    {
        var relationalAssembly = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, RelationalAssemblyName, StringComparison.Ordinal));

        relationalAssembly ??= anchor.GetType().Assembly
            .GetReferencedAssemblies()
            .Where(reference =>
                string.Equals(reference.Name, RelationalAssemblyName, StringComparison.Ordinal))
            .Select(reference =>
            {
                try
                {
                    return Assembly.Load(reference);
                }
                catch
                {
                    return null;
                }
            })
            .FirstOrDefault(loaded => loaded is not null);

        if (relationalAssembly is null)
        {
            return null;
        }

        var cacheKey = relationalAssembly.FullName ?? RelationalAssemblyName;

        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var entityExtensionsType = relationalAssembly.GetType(EntityTypeExtensionsTypeName, false);
        var propertyExtensionsType = relationalAssembly.GetType(PropertyExtensionsTypeName, false);

        if (entityExtensionsType is null || propertyExtensionsType is null)
        {
            Cache[cacheKey] = null;
            return null;
        }

        var resolved = RelationalMetadataMethods.TryCreate(entityExtensionsType, propertyExtensionsType);
        Cache[cacheKey] = resolved;

        return resolved;
    }

    internal static IEnumerable<object> EnumerateEntityTypes(object modelBuilder)
    {
        var model = modelBuilder.GetType()
            .GetProperty("Model", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(modelBuilder);

        if (model is null)
        {
            yield break;
        }

        var getEntityTypes = model.GetType()
            .GetInterfaces()
            .FirstOrDefault(iface => string.Equals(iface.FullName, IModelFullName, StringComparison.Ordinal))
            ?.GetMethod(
                "GetEntityTypes",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);

        if (getEntityTypes?.Invoke(model, null) is not IEnumerable entityTypes)
        {
            yield break;
        }

        foreach (var entityType in entityTypes)
        {
            if (entityType is not null)
            {
                yield return entityType;
            }
        }
    }

    internal static IEnumerable<object> EnumerateProperties(object entityType)
    {
        var getProperties = entityType.GetType()
            .GetInterfaces()
            .FirstOrDefault(iface => string.Equals(iface.FullName, IEntityTypeFullName, StringComparison.Ordinal))
            ?.GetMethod("GetProperties", BindingFlags.Public | BindingFlags.Instance);

        if (getProperties?.Invoke(entityType, null) is not IEnumerable properties)
        {
            yield break;
        }

        foreach (var property in properties)
        {
            if (property is not null)
            {
                yield return property;
            }
        }
    }

    internal static string? GetPropertyName(object property)
    {
        return property.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(property) as string;
    }

    internal static void LowercaseColumnNames(RelationalMetadataMethods relationalMetadata, object entityType)
    {
        TransformColumnNames(relationalMetadata, entityType, static name => name.ToLowerInvariant());
    }

    internal static void UppercaseColumnNames(RelationalMetadataMethods relationalMetadata, object entityType)
    {
        TransformColumnNames(relationalMetadata, entityType, static name => name.ToUpperInvariant());
    }

    private static void TransformColumnNames(
        RelationalMetadataMethods relationalMetadata,
        object entityType,
        Func<string, string> transform)
    {
        foreach (var property in EnumerateProperties(entityType))
        {
            var columnName = relationalMetadata.GetColumnName(property);

            if (string.IsNullOrEmpty(columnName))
            {
                continue;
            }

            relationalMetadata.SetColumnName(property, transform(columnName));
        }
    }

    internal sealed class RelationalMetadataMethods(
        MethodInfo getSchema,
        MethodInfo setSchema,
        MethodInfo getTableName,
        MethodInfo setTableName,
        MethodInfo getColumnName,
        MethodInfo setColumnName)
    {
        internal static RelationalMetadataMethods? TryCreate(Type entityExtensionsType, Type propertyExtensionsType)
        {
            var getSchema = FindExtensionMethod(entityExtensionsType, "GetSchema", 1);
            var setSchema = FindExtensionMethod(entityExtensionsType, "SetSchema", 2);
            var getTableName = FindExtensionMethod(entityExtensionsType, "GetTableName", 1);
            var setTableName = FindExtensionMethod(entityExtensionsType, "SetTableName", 2);
            var getColumnName = FindExtensionMethod(propertyExtensionsType, "GetColumnName", 1);
            var setColumnName = FindExtensionMethod(propertyExtensionsType, "SetColumnName", 2);

            if (getSchema is null
                || setSchema is null
                || getTableName is null
                || setTableName is null
                || getColumnName is null
                || setColumnName is null)
            {
                return null;
            }

            return new RelationalMetadataMethods(
                getSchema,
                setSchema,
                getTableName,
                setTableName,
                getColumnName,
                setColumnName);
        }

        internal string? GetSchema(object entityType)
        {
            return getSchema.Invoke(null, [entityType]) as string;
        }

        internal void SetSchema(object entityType, string? schema)
        {
            setSchema.Invoke(null, [entityType, schema]);
        }

        internal string? GetTableName(object entityType)
        {
            return getTableName.Invoke(null, [entityType]) as string;
        }

        internal void SetTableName(object entityType, string tableName)
        {
            setTableName.Invoke(null, [entityType, tableName]);
        }

        internal string? GetColumnName(object property)
        {
            return getColumnName.Invoke(null, [property]) as string;
        }

        internal void SetColumnName(object property, string columnName)
        {
            setColumnName.Invoke(null, [property, columnName]);
        }

        private static MethodInfo? FindExtensionMethod(Type extensionsType, string methodName, int parameterCount)
        {
            foreach (var method in extensionsType.GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (method.GetParameters().Length != parameterCount)
                {
                    continue;
                }

                return method;
            }

            return null;
        }
    }
}