using System.Linq.Expressions;
using System.Reflection;

namespace MyEfVibe;

/// <summary>
///     Uppercases schema, table, and column names for Oracle AdventureWorks dumps where objects
///     are stored as <c>PRODUCTION.PRODUCT</c> while EF maps <c>Production.Product</c>, and applies
///     <c>Guid</c> ↔ <c>string</c> conversions when <c>ROWGUID</c> columns are <c>VARCHAR2</c>.
/// </summary>
public static class OracleRelationalNamingApplier
{
    public static void CustomizeAfterBase(object modelBuilder, object context)
    {
        CustomizeAfterBase(modelBuilder, context, registrationId: 0);
    }

    public static void CustomizeAfterBase(object modelBuilder, object context, long registrationId)
    {
        Apply(modelBuilder);
        ApplyGuidConversions(modelBuilder, AdventureWorksColumnMetadataCache.TryGet(registrationId));
    }

    internal static void Apply(object modelBuilder)
    {
        var relationalMetadata = RelationalMetadataReflection.Resolve(modelBuilder);

        if (relationalMetadata is null)
        {
            return;
        }

        foreach (var entityType in RelationalMetadataReflection.EnumerateEntityTypes(modelBuilder))
        {
            var schema = relationalMetadata.GetSchema(entityType);

            if (!string.IsNullOrEmpty(schema))
            {
                relationalMetadata.SetSchema(entityType, schema.ToUpperInvariant());
            }

            var tableName = relationalMetadata.GetTableName(entityType);

            if (!string.IsNullOrEmpty(tableName))
            {
                relationalMetadata.SetTableName(entityType, tableName.ToUpperInvariant());
            }

            RelationalMetadataReflection.UppercaseColumnNames(relationalMetadata, entityType);
        }
    }

    internal static bool RequiresGuidStringConversion(Type? clrType, string? dataType)
    {
        return clrType == typeof(Guid) && IsOracleStringType(dataType);
    }

    internal static bool IsOracleStringType(string? dataType)
    {
        if (string.IsNullOrEmpty(dataType))
        {
            return false;
        }

        return string.Equals(dataType, "VARCHAR2", StringComparison.OrdinalIgnoreCase)
               || string.Equals(dataType, "NVARCHAR2", StringComparison.OrdinalIgnoreCase)
               || string.Equals(dataType, "CHAR", StringComparison.OrdinalIgnoreCase)
               || string.Equals(dataType, "NCHAR", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyGuidConversions(
        object modelBuilder,
        Dictionary<(string Schema, string Table), Dictionary<string, string>>? columnIndex)
    {
        if (columnIndex is null || columnIndex.Count == 0)
        {
            return;
        }

        var relationalMetadata = RelationalMetadataReflection.Resolve(modelBuilder);

        if (relationalMetadata is null)
        {
            return;
        }

        foreach (var entityType in RelationalMetadataReflection.EnumerateEntityTypes(modelBuilder))
        {
            var schema = relationalMetadata.GetSchema(entityType);
            var tableName = relationalMetadata.GetTableName(entityType);

            if (string.IsNullOrEmpty(schema) || string.IsNullOrEmpty(tableName))
            {
                continue;
            }

            if (!columnIndex.TryGetValue((schema, tableName), out var columns))
            {
                continue;
            }

            var entityClrType = TryGetClrType(entityType);

            if (entityClrType is null)
            {
                continue;
            }

            foreach (var property in RelationalMetadataReflection.EnumerateProperties(entityType))
            {
                var propertyName = RelationalMetadataReflection.GetPropertyName(property);

                if (string.IsNullOrEmpty(propertyName))
                {
                    continue;
                }

                var columnName = relationalMetadata.GetColumnName(property) ?? propertyName;

                if (!columns.TryGetValue(columnName, out var dataType))
                {
                    continue;
                }

                var propertyClrType = entityClrType.GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.PropertyType;

                if (!RequiresGuidStringConversion(propertyClrType, dataType))
                {
                    continue;
                }

                TryApplyGuidStringConversion(modelBuilder, entityClrType, propertyName);
            }
        }
    }

    private static Type? TryGetClrType(object metadata)
    {
        foreach (var declaringType in EnumerateMetadataTypes(metadata))
        {
            if (declaringType.GetProperty("ClrType", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(metadata) is Type clrType)
            {
                return clrType;
            }
        }

        return null;
    }

    private static IEnumerable<Type> EnumerateMetadataTypes(object metadata)
    {
        yield return metadata.GetType();

        foreach (var iface in metadata.GetType().GetInterfaces())
        {
            yield return iface;
        }
    }

    private static void TryApplyGuidStringConversion(
        object modelBuilder,
        Type entityClrType,
        string propertyName)
    {
        var propertyInfo = entityClrType.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (propertyInfo is null)
        {
            return;
        }

        var entityBuilder = modelBuilder.GetType()
            .GetMethod("Entity", [typeof(Type)])?
            .Invoke(modelBuilder, [entityClrType]);

        if (entityBuilder is null)
        {
            return;
        }

        var propertyBuilder = entityBuilder.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method =>
                string.Equals(method.Name, "Property", StringComparison.Ordinal)
                && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType == typeof(string))?
            .Invoke(entityBuilder, [propertyName])
            ?? entityBuilder.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, "Property", StringComparison.Ordinal)
                    && method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType == typeof(PropertyInfo))?
                .Invoke(entityBuilder, [propertyInfo]);

        if (propertyBuilder is null)
        {
            return;
        }

        foreach (var method in propertyBuilder.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!string.Equals(method.Name, "HasConversion", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                if (method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
                {
                    method.MakeGenericMethod(typeof(string)).Invoke(propertyBuilder, null);
                    return;
                }

                if (method.GetParameters().Length == 2
                    && method.GetParameters()[0].ParameterType == typeof(Expression<Func<Guid, string>>)
                    && method.GetParameters()[1].ParameterType == typeof(Expression<Func<string, Guid>>))
                {
                    method.Invoke(propertyBuilder, [BuildGuidToStringExpression(), BuildStringToGuidExpression()]);
                    return;
                }
            }
            catch
            {
                // Try the next HasConversion overload.
            }
        }
    }

    private static Expression<Func<Guid, string>> BuildGuidToStringExpression()
    {
        var value = Expression.Parameter(typeof(Guid), "value");

        return Expression.Lambda<Func<Guid, string>>(
            Expression.Call(value, nameof(Guid.ToString), Type.EmptyTypes),
            value);
    }

    private static Expression<Func<string, Guid>> BuildStringToGuidExpression()
    {
        var value = Expression.Parameter(typeof(string), "value");

        return Expression.Lambda<Func<string, Guid>>(
            Expression.Call(
                typeof(Guid).GetMethod(nameof(Guid.Parse), [typeof(string)])!,
                value),
            value);
    }
}
