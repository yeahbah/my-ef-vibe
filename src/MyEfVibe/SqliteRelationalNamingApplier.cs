namespace MyEfVibe;

/// <summary>
///     Maps SQL Server-style schema/table pairs to SQLite <c>Schema.Table</c> physical names (see DuckDB conversion)
///     and lowercases column names to match pgloader-derived SQLite files.
/// </summary>
public static class SqliteRelationalNamingApplier
{
    public static void CustomizeAfterBase(object modelBuilder, object context)
    {
        Apply(modelBuilder);
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
            var tableName = relationalMetadata.GetTableName(entityType);

            if (!string.IsNullOrEmpty(schema) && !string.IsNullOrEmpty(tableName))
            {
                relationalMetadata.SetTableName(entityType, $"{schema}.{tableName}");
                relationalMetadata.SetSchema(entityType, null);
            }

            RelationalMetadataReflection.LowercaseColumnNames(relationalMetadata, entityType);
        }
    }
}