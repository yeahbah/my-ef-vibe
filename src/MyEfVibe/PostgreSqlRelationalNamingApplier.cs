namespace MyEfVibe;

/// <summary>
///     Lowercases schema, table, and column names for PostgreSQL (matches AdventureWorks sample
///     <c>PostgreSqlRelationalNaming</c>). Uses reflection so myefvibe does not compile against EF Core.
/// </summary>
public static class PostgreSqlRelationalNamingApplier
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

            if (!string.IsNullOrEmpty(schema))
            {
                relationalMetadata.SetSchema(entityType, schema.ToLowerInvariant());
            }

            var tableName = relationalMetadata.GetTableName(entityType);

            if (!string.IsNullOrEmpty(tableName))
            {
                relationalMetadata.SetTableName(entityType, tableName.ToLowerInvariant());
            }

            RelationalMetadataReflection.LowercaseColumnNames(relationalMetadata, entityType);
        }
    }
}