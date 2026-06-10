using System.Linq.Expressions;
using System.Reflection;

namespace MyEfVibe;

/// <summary>
///     Maps EF property names to AdventureWorks PostgreSQL column names (for example
///     <c>ProductId</c> to <c>ProductID</c>) using a live <c>information_schema</c> index.
/// </summary>
public static class PostgreSqlAdventureWorksNamingApplier
{
    private static Dictionary<(string Schema, string Table), Dictionary<string, string>>? _columnIndex;

    internal static void SetColumnIndex(
        Dictionary<(string Schema, string Table), Dictionary<string, string>> columnIndex)
    {
        _columnIndex = columnIndex;
    }

    public static void CustomizeAfterBase(object modelBuilder, object context)
    {
        ApplyBoolConversions(modelBuilder);
        ApplyColumnRenames(modelBuilder);
    }

    internal static void Apply(object modelBuilder)
    {
        ApplyBoolConversions(modelBuilder);
        ApplyColumnRenames(modelBuilder);
    }

    private static void ApplyColumnRenames(object modelBuilder)
    {
        var columnIndex = _columnIndex;

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

            var columnNames = new HashSet<string>(columns.Keys, StringComparer.Ordinal);

            foreach (var property in RelationalMetadataReflection.EnumerateProperties(entityType))
            {
                var propertyName = RelationalMetadataReflection.GetPropertyName(property);

                if (string.IsNullOrEmpty(propertyName))
                {
                    continue;
                }

                var mappedColumn = ResolveColumnName(propertyName, columnNames);

                if (mappedColumn is not null)
                {
                    relationalMetadata.SetColumnName(property, mappedColumn);
                }
            }
        }
    }

    private static void ApplyBoolConversions(object modelBuilder)
    {
        var columnIndex = _columnIndex;

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
                var mappedColumn = ResolveColumnName(propertyName, new HashSet<string>(columns.Keys, StringComparer.Ordinal))
                                   ?? columnName;

                var propertyClrType = entityClrType.GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.PropertyType;

                if (!RequiresBoolSmallintConversion(
                        propertyClrType,
                        columns.TryGetValue(mappedColumn, out var dataType) ? dataType : null))
                {
                    continue;
                }

                TryApplyBoolSmallintConversion(modelBuilder, entityClrType, propertyName);
            }
        }
    }

    internal static bool RequiresBoolSmallintConversion(Type? clrType, string? dataType)
    {
        return clrType == typeof(bool)
               && string.Equals(dataType, "smallint", StringComparison.OrdinalIgnoreCase);
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

    private static void TryApplyBoolSmallintConversion(
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
                    method.MakeGenericMethod(typeof(short)).Invoke(propertyBuilder, null);
                    return;
                }

                if (method.GetParameters().Length == 2
                    && method.GetParameters()[0].ParameterType == typeof(Expression<Func<bool, short>>)
                    && method.GetParameters()[1].ParameterType == typeof(Expression<Func<short, bool>>))
                {
                    method.Invoke(propertyBuilder, [BuildBoolToShortExpression(), BuildShortToBoolExpression()]);
                    return;
                }
            }
            catch
            {
                // Try the next HasConversion overload.
            }
        }
    }

    private static Expression<Func<bool, short>> BuildBoolToShortExpression()
    {
        var value = Expression.Parameter(typeof(bool), "value");

        return Expression.Lambda<Func<bool, short>>(
            Expression.Condition(
                value,
                Expression.Constant((short)1),
                Expression.Constant((short)0)),
            value);
    }

    private static Expression<Func<short, bool>> BuildShortToBoolExpression()
    {
        var value = Expression.Parameter(typeof(short), "value");

        return Expression.Lambda<Func<short, bool>>(
            Expression.NotEqual(value, Expression.Constant((short)0)),
            value);
    }

    internal static string? ResolveColumnName(string propertyName, HashSet<string> columns)
    {
        if (propertyName.EndsWith("Id", StringComparison.Ordinal) && propertyName.Length > 2)
        {
            var idVariant = string.Concat(propertyName.AsSpan(0, propertyName.Length - 2), "ID");

            if (columns.Contains(idVariant))
            {
                return idVariant;
            }

            if (columns.Contains(propertyName))
            {
                return propertyName;
            }

            return null;
        }

        if (string.Equals(propertyName, "Rowguid", StringComparison.Ordinal))
        {
            if (columns.Contains("rowguid"))
            {
                return "rowguid";
            }

            if (columns.Contains("Rowguid"))
            {
                return "Rowguid";
            }
        }

        return null;
    }
}
