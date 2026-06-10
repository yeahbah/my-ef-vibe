namespace MyEfVibe;

/// <summary>
///     Uppercases schema, table, and column names for Oracle AdventureWorks dumps where objects
///     are stored as <c>PRODUCTION.PRODUCT</c> while EF maps <c>Production.Product</c>.
/// </summary>
public static class OracleRelationalNamingApplier
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
}
