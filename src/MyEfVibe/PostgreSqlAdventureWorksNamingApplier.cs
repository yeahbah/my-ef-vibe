namespace MyEfVibe;

/// <summary>
///     Maps EF property names to AdventureWorks PostgreSQL column names (for example
///     <c>ProductId</c> to <c>ProductID</c>) using a live <c>information_schema</c> index.
/// </summary>
public static class PostgreSqlAdventureWorksNamingApplier
{
    private static Dictionary<(string Schema, string Table), HashSet<string>>? _columnIndex;

    internal static void SetColumnIndex(Dictionary<(string Schema, string Table), HashSet<string>> columnIndex)
    {
        _columnIndex = columnIndex;
    }

    public static void CustomizeAfterBase(object modelBuilder, object context)
    {
        Apply(modelBuilder);
    }

    internal static void Apply(object modelBuilder)
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

            foreach (var property in RelationalMetadataReflection.EnumerateProperties(entityType))
            {
                var propertyName = RelationalMetadataReflection.GetPropertyName(property);

                if (string.IsNullOrEmpty(propertyName))
                {
                    continue;
                }

                var mappedColumn = ResolveColumnName(propertyName, columns);

                if (mappedColumn is null)
                {
                    continue;
                }

                relationalMetadata.SetColumnName(property, mappedColumn);
            }
        }
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
