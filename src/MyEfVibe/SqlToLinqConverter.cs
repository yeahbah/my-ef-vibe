using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MyEfVibe;

internal static partial class SqlToLinqConverter
{
    internal sealed class SqlToLinqMapping
    {
        public string Table { get; init; } = string.Empty;

        public string DbSet { get; init; } = string.Empty;

        public string Entity { get; init; } = string.Empty;
    }

    internal sealed class SqlToLinqDraft
    {
        public string Linq { get; init; } = string.Empty;

        public string Confidence { get; init; } = "low";

        public IReadOnlyList<string> Unsupported { get; init; } = [];

        public IReadOnlyList<SqlToLinqMapping> Mappings { get; init; } = [];

        public string? TranslatedSql { get; set; }

        public double? Similarity { get; set; }
    }

    internal static SqlToLinqDraft Convert(object dbContext, string sql)
    {
        var unsupported = new List<string>();
        var mappings = new List<SqlToLinqMapping>();
        var trimmed = sql.Trim().TrimEnd(';');

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new SqlToLinqDraft
            {
                Linq = string.Empty,
                Confidence = "low",
                Unsupported = ["Empty SQL input."],
                Mappings = mappings
            };
        }

        if (JoinRegex().IsMatch(trimmed))
        {
            unsupported.Add("JOIN clauses need manual navigation mapping.");
        }

        if (CteRegex().IsMatch(trimmed))
        {
            unsupported.Add("CTE (WITH) is not supported in draft conversion.");
        }

        var fromMatch = FromRegex().Match(trimmed);
        if (!fromMatch.Success)
        {
            return new SqlToLinqDraft
            {
                Linq = "// Could not find FROM clause — paste a simple SELECT query.",
                Confidence = "low",
                Unsupported = ["No FROM clause detected."],
                Mappings = mappings
            };
        }

        var tableToken = NormalizeTableToken(fromMatch.Groups[1].Value);
        if (!TryResolveDbSet(dbContext, tableToken, out var dbSet, out var entityType, out var mappingNote))
        {
            unsupported.Add(mappingNote ?? $"Could not map table `{tableToken}` to a DbSet.");
            return new SqlToLinqDraft
            {
                Linq = $"// TODO: map `{tableToken}` to db.<DbSet>",
                Confidence = "low",
                Unsupported = unsupported,
                Mappings = mappings
            };
        }

        mappings.Add(new SqlToLinqMapping
        {
            Table = tableToken,
            DbSet = $"db.{dbSet}",
            Entity = entityType!.Name
        });

        var builder = new StringBuilder($"db.{dbSet}");
        var lambdaParam = "x";

        var whereMatch = WhereRegex().Match(trimmed);
        if (whereMatch.Success)
        {
            var predicate = whereMatch.Groups[1].Value.Trim();
            if (TryBuildWhereClause(entityType!, lambdaParam, predicate, out var whereClause, out var whereNote))
            {
                builder.Append(whereClause);
            }
            else
            {
                unsupported.Add(whereNote ?? $"WHERE clause needs manual rewrite: {predicate}");
                builder.Append("\n    // TODO: rewrite WHERE manually");
            }
        }

        var orderMatch = OrderByRegex().Match(trimmed);
        if (orderMatch.Success)
        {
            var column = NormalizeColumnToken(orderMatch.Groups[1].Value);
            var property = ResolvePropertyName(entityType!, column);
            if (property is null)
            {
                unsupported.Add($"ORDER BY column `{column}` was not found on `{entityType!.Name}`.");
            }
            else
            {
                var direction = string.Equals(orderMatch.Groups[2].Value, "desc", StringComparison.OrdinalIgnoreCase)
                    ? "Descending"
                    : string.Empty;
                builder.Append($".OrderBy{direction}({lambdaParam} => {lambdaParam}.{property})");
            }
        }

        var take = TopRegex().Match(trimmed).Groups[1].Value;
        if (string.IsNullOrWhiteSpace(take))
        {
            take = LimitRegex().Match(trimmed).Groups[1].Value;
        }

        if (!string.IsNullOrWhiteSpace(take))
        {
            builder.Append($".Take({take})");
        }

        if (SelectStarRegex().IsMatch(trimmed))
        {
            builder.Append(".ToList();");
        }
        else if (HasExplicitProjection(trimmed))
        {
            builder.Append("\n    .Select(x => new { /* map columns */ })\n    .ToList();");
            unsupported.Add("Projection columns need manual mapping in Select.");
        }
        else
        {
            builder.Append(".ToList();");
        }

        var confidence = unsupported.Count == 0 ? "high" : unsupported.Count <= 2 ? "partial" : "low";

        return new SqlToLinqDraft
        {
            Linq = builder.ToString(),
            Confidence = confidence,
            Unsupported = unsupported,
            Mappings = mappings
        };
    }

    private static bool TryResolveDbSet(
        object dbContext,
        string tableToken,
        out string dbSetName,
        out Type? entityType,
        out string? note)
    {
        dbSetName = string.Empty;
        entityType = null;
        note = null;

        var index = BuildTableIndex(dbContext);

        if (index.TryGetValue(tableToken, out var match))
        {
            dbSetName = match.DbSetName;
            entityType = match.EntityType;
            return true;
        }

        note = $"No DbSet matches table `{tableToken}`.";
        return false;
    }

    private static Dictionary<string, (string DbSetName, Type EntityType)> BuildTableIndex(object dbContext)
    {
        var index = new Dictionary<string, (string DbSetName, Type EntityType)>(StringComparer.OrdinalIgnoreCase);
        var relational = RelationalMetadataReflection.Resolve(dbContext);

        foreach (var entry in EntityDescriptor.EnumerateDbSetEntities(dbContext))
        {
            index[entry.DbSetName] = entry;
            index[entry.EntityType.Name] = entry;

            var modelEntity = EntityDescriptor.TryFindModelEntity(dbContext, entry.EntityType);
            var tableName = modelEntity is not null ? relational?.GetTableName(modelEntity) : null;

            if (!string.IsNullOrWhiteSpace(tableName))
            {
                index[tableName] = entry;

                var schemaSeparator = tableName.IndexOf('.');
                if (schemaSeparator >= 0 && schemaSeparator < tableName.Length - 1)
                {
                    index[tableName[(schemaSeparator + 1)..]] = entry;
                }
            }
        }

        return index;
    }

    private static bool TryBuildWhereClause(
        Type entityType,
        string lambdaParam,
        string predicate,
        out string clause,
        out string? note)
    {
        clause = string.Empty;
        note = null;

        if (predicate.Contains(" or ", StringComparison.OrdinalIgnoreCase)
            || predicate.Contains(" and ", StringComparison.OrdinalIgnoreCase)
            || predicate.Contains('('))
        {
            note = $"WHERE clause needs manual rewrite: {predicate}";
            return false;
        }

        var equalsIndex = predicate.IndexOf('=');
        if (equalsIndex < 0)
        {
            note = $"Unsupported WHERE predicate: {predicate}";
            return false;
        }

        var column = NormalizeColumnToken(predicate[..equalsIndex]);
        var value = predicate[(equalsIndex + 1)..].Trim();
        var property = ResolvePropertyName(entityType, column);

        if (property is null)
        {
            note = $"Column `{column}` was not found on `{entityType.Name}`.";
            return false;
        }

        clause = $".Where({lambdaParam} => {lambdaParam}.{property} == {FormatSqlValue(value)})";
        return true;
    }

    private static string? ResolvePropertyName(Type entityType, string columnName)
    {
        var cleaned = NormalizeColumnToken(columnName);
        var property = entityType
            .GetProperties()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, cleaned, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Name, ToPascalCase(cleaned), StringComparison.OrdinalIgnoreCase));

        return property?.Name;
    }

    private static string FormatSqlValue(string rawValue)
    {
        var trimmed = rawValue.Trim();

        if ((trimmed.StartsWith('\'') && trimmed.EndsWith('\''))
            || (trimmed.StartsWith('"') && trimmed.EndsWith('"')))
        {
            var inner = trimmed[1..^1];
            return $"\"{inner}\"";
        }

        if (decimal.TryParse(trimmed, out _))
        {
            return trimmed;
        }

        return $"\"{trimmed}\"";
    }

    private static string NormalizeTableToken(string token)
    {
        return token.Trim().Trim('[', ']').Split('.').Last();
    }

    private static string NormalizeColumnToken(string token)
    {
        return token.Trim().Trim('[', ']').Split('.').Last();
    }

    private static string ToPascalCase(string value)
    {
        var parts = value.Split(['_', ' '], StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(static part =>
            part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));
    }

    [GeneratedRegex(@"\bjoin\b", RegexOptions.IgnoreCase)]
    private static partial Regex JoinRegex();

    [GeneratedRegex(@"^\s*with\b", RegexOptions.IgnoreCase)]
    private static partial Regex CteRegex();

    [GeneratedRegex(@"\bfrom\s+([^\s,;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex FromRegex();

    [GeneratedRegex(@"\bwhere\s+(.+?)(?:\border\s+by\b|\blimit\b|\boffset\b|\btop\b|$)", RegexOptions.IgnoreCase)]
    private static partial Regex WhereRegex();

    [GeneratedRegex(@"\border\s+by\s+([^\s,;]+)(?:\s+(asc|desc))?", RegexOptions.IgnoreCase)]
    private static partial Regex OrderByRegex();

    [GeneratedRegex(@"\btop\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex TopRegex();

    [GeneratedRegex(@"\blimit\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex LimitRegex();

    [GeneratedRegex(@"\bselect\s+\*", RegexOptions.IgnoreCase)]
    private static partial Regex SelectStarRegex();

    private static bool HasExplicitProjection(string sql)
    {
        var match = SelectClauseRegex().Match(sql);
        if (!match.Success)
        {
            return false;
        }

        var clause = match.Groups[1].Value.Trim();
        return clause.Length > 0 && !clause.Equals("*", StringComparison.Ordinal) && !clause.Contains('*');
    }

    [GeneratedRegex(@"\bselect\s+(.+?)\s+from\b", RegexOptions.IgnoreCase)]
    private static partial Regex SelectClauseRegex();
}
