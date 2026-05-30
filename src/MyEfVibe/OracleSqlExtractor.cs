using System.Globalization;
using System.Text.RegularExpressions;

namespace MyEfVibe;

/// <summary>
///     Extracts a plain SELECT from Oracle EF Core <c>ToQueryString()</c> PL/SQL blocks for EXPLAIN PLAN.
/// </summary>
internal static partial class OracleSqlExtractor
{
    internal static string? TryExtractExplainableSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return null;
        }

        var trimmed = sql.Trim();

        if (!LooksLikePlSqlBlock(trimmed))
        {
            return trimmed.Trim().TrimEnd(';');
        }

        var select = TryExtractDbmsSqlAssignment(trimmed);

        if (string.IsNullOrWhiteSpace(select))
        {
            return null;
        }

        select = InlineDbmsSqlBindVariables(trimmed, select);

        return select.Trim().TrimEnd(';');
    }

    private static bool LooksLikePlSqlBlock(string sql)
    {
        return sql.Contains("BEGIN", StringComparison.OrdinalIgnoreCase)
               && (sql.Contains("dbms_sql", StringComparison.OrdinalIgnoreCase)
                   || sql.Contains("l_sql", StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryExtractDbmsSqlAssignment(string plSql)
    {
        var match = DbmsSqlAssignmentRegex().Match(plSql);

        if (!match.Success)
        {
            return null;
        }

        return UnescapeOracleStringLiteral(match.Groups["sql"].Value);
    }

    private static string InlineDbmsSqlBindVariables(string plSql, string select)
    {
        var result = select;

        foreach (Match match in DbmsSqlBindVariableRegex().Matches(plSql))
        {
            var name = match.Groups["name"].Value;
            var value = match.Groups["value"].Value.Trim();

            result = result.Replace(name, FormatBindValue(value), StringComparison.Ordinal);
        }

        return result;
    }

    private static string UnescapeOracleStringLiteral(string value)
    {
        return value.Replace("''", "'", StringComparison.Ordinal);
    }

    private static string FormatBindValue(string value)
    {
        if (value.StartsWith('\'') && value.EndsWith('\''))
        {
            return value;
        }

        if (long.TryParse(value, out _) || decimal.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out _))
        {
            return value;
        }

        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    [GeneratedRegex(
        @"l_sql\s*:=\s*'(?<sql>(?:[^']|'')*)'",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DbmsSqlAssignmentRegex();

    [GeneratedRegex(
        @"dbms_sql\.bind_variable\s*\(\s*\w+\s*,\s*'(?<name>:[^']+)'\s*,\s*(?<value>[^)]+)\)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DbmsSqlBindVariableRegex();
}