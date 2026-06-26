using System.Data.Common;
using System.Reflection;

namespace MyEfVibe;

/// <summary>
///     Maps SQL table tokens to DbSet properties on the active <see cref="Microsoft.EntityFrameworkCore.DbContext"/>.
///     DbSets are discovered via reflection (same source as <c>:tables</c>), then aliases are added from EF
///     relational metadata and, when available, live database catalog names.
/// </summary>
internal static class DbSetTableIndexBuilder
{
    internal sealed record DbSetTableEntry(
        string DbSetName,
        Type EntityType,
        string? Schema,
        string? TableName);

    internal static Dictionary<string, DbSetTableEntry> Build(object dbContext)
    {
        var entries = CollectDbSetEntries(dbContext);
        var index = new Dictionary<string, DbSetTableEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            RegisterEntryAliases(index, entry);
        }

        TryEnrichFromLiveDatabase(dbContext, entries, index);

        return index;
    }

    private static List<DbSetTableEntry> CollectDbSetEntries(object dbContext)
    {
        var relational = RelationalMetadataReflection.Resolve(dbContext);
        var entries = new List<DbSetTableEntry>();

        foreach (var (dbSetName, entityType) in EntityDescriptor.EnumerateDbSetEntities(dbContext))
        {
            var modelEntity = EntityDescriptor.TryFindModelEntity(dbContext, entityType);
            var tableName = modelEntity is not null ? relational?.GetTableName(modelEntity) : null;
            var schema = modelEntity is not null ? relational?.GetSchema(modelEntity) : null;

            entries.Add(new DbSetTableEntry(dbSetName, entityType, schema, tableName));
        }

        return entries;
    }

    private static void RegisterEntryAliases(
        IDictionary<string, DbSetTableEntry> index,
        DbSetTableEntry entry)
    {
        TryAdd(index, entry.DbSetName, entry);
        TryAdd(index, entry.EntityType.Name, entry);

        if (!string.IsNullOrWhiteSpace(entry.TableName))
        {
            TryAdd(index, entry.TableName, entry);

            if (!string.IsNullOrWhiteSpace(entry.Schema))
            {
                TryAdd(index, $"{entry.Schema}.{entry.TableName}", entry);
            }

            var schemaSeparator = entry.TableName.IndexOf('.');
            if (schemaSeparator >= 0 && schemaSeparator < entry.TableName.Length - 1)
            {
                TryAdd(index, entry.TableName[(schemaSeparator + 1)..], entry);
            }
        }

        if (entry.DbSetName.EndsWith("s", StringComparison.OrdinalIgnoreCase) && entry.DbSetName.Length > 1)
        {
            TryAdd(index, entry.DbSetName[..^1], entry);
        }

        if (!string.IsNullOrWhiteSpace(entry.TableName))
        {
            TryAdd(index, ToSingular(entry.TableName), entry);
            TryAdd(index, ToPlural(entry.TableName), entry);
        }
    }

    private static void TryEnrichFromLiveDatabase(
        object dbContext,
        IReadOnlyList<DbSetTableEntry> entries,
        IDictionary<string, DbSetTableEntry> index)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var database = dbContext.GetType().GetProperty("Database")?.GetValue(dbContext);

        if (database is null)
        {
            return;
        }

        if (!RelationalDatabaseFacadeInvoker.TryGetDbConnection(
                database,
                AppDomain.CurrentDomain.GetAssemblies(),
                out var connection)
            || connection is not DbConnection dbConnection)
        {
            return;
        }

        foreach (var tableRef in LiveDatabaseTableCatalog.TryListTables(dbConnection))
        {
            if (!TryResolveEntryForLiveTable(entries, tableRef, out var match))
            {
                continue;
            }

            TryAdd(index, tableRef.TableName, match);

            if (!string.IsNullOrWhiteSpace(tableRef.Schema))
            {
                TryAdd(index, $"{tableRef.Schema}.{tableRef.TableName}", match);
            }
        }
    }

    private static bool TryResolveEntryForLiveTable(
        IReadOnlyList<DbSetTableEntry> entries,
        LiveDatabaseTableCatalog.TableRef tableRef,
        out DbSetTableEntry match)
    {
        foreach (var entry in entries)
        {
            if (TableRefMatchesEntry(tableRef, entry))
            {
                match = entry;
                return true;
            }
        }

        match = default!;
        return false;
    }

    private static bool TableRefMatchesEntry(LiveDatabaseTableCatalog.TableRef tableRef, DbSetTableEntry entry)
    {
        if (NamesEqual(entry.TableName, tableRef.TableName)
            && SchemasEqual(entry.Schema, tableRef.Schema))
        {
            return true;
        }

        if (NamesEqual(entry.TableName, tableRef.TableName) && string.IsNullOrWhiteSpace(entry.Schema))
        {
            return true;
        }

        if (NamesEqual(entry.DbSetName, tableRef.TableName)
            || NamesEqual(entry.EntityType.Name, tableRef.TableName)
            || NamesEqual(ToSingular(entry.DbSetName), tableRef.TableName)
            || NamesEqual(ToPlural(entry.EntityType.Name), tableRef.TableName)
            || NamesEqual(ToSingular(entry.EntityType.Name), tableRef.TableName))
        {
            return SchemasEqual(entry.Schema, tableRef.Schema) || string.IsNullOrWhiteSpace(tableRef.Schema);
        }

        if (!string.IsNullOrWhiteSpace(tableRef.Schema)
            && NamesEqual(entry.TableName, tableRef.TableName)
            && string.IsNullOrWhiteSpace(entry.Schema))
        {
            return true;
        }

        return false;
    }

    private static void TryAdd(IDictionary<string, DbSetTableEntry> index, string? key, DbSetTableEntry entry)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        index.TryAdd(key.Trim(), entry);
    }

    private static bool NamesEqual(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left)
               && !string.IsNullOrWhiteSpace(right)
               && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SchemasEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return true;
        }

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToSingular(string value)
    {
        if (value.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && value.Length > 3)
        {
            return value[..^3] + "y";
        }

        if (value.EndsWith("s", StringComparison.OrdinalIgnoreCase) && value.Length > 1)
        {
            return value[..^1];
        }

        return value;
    }

    private static string ToPlural(string value)
    {
        if (value.EndsWith("y", StringComparison.OrdinalIgnoreCase) && value.Length > 1)
        {
            return value[..^1] + "ies";
        }

        if (!value.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            return value + "s";
        }

        return value;
    }
}
