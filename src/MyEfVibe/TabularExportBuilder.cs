using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace MyEfVibe;

internal static class TabularExportBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static string ToCsv(IReadOnlyList<object?> rows)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var table = BuildTable(rows);
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", table.Columns.Select(EscapeCsv)));

        foreach (var row in table.Rows)
        {
            builder.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        }

        return builder.ToString();
    }

    internal static string ToJson(IReadOnlyList<object?> rows)
    {
        if (rows.Count == 0)
        {
            return "[]";
        }

        var table = BuildTable(rows);
        var records = new List<Dictionary<string, string>>(table.Rows.Count);

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var record = new Dictionary<string, string>(table.Columns.Count, StringComparer.Ordinal);

            for (var columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                record[table.Columns[columnIndex]] = table.Rows[rowIndex][columnIndex];
            }

            records.Add(record);
        }

        return JsonSerializer.Serialize(records, JsonOptions);
    }

    private static ExportTable BuildTable(IReadOnlyList<object?> rows)
    {
        var columnSet = new SortedSet<string>(StringComparer.Ordinal);
        var propertyMaps = new List<IReadOnlyDictionary<string, PropertyInfo>>();

        foreach (var row in rows)
        {
            var properties = GetReadableProperties(row);

            propertyMaps.Add(properties.ToDictionary(static pair => pair.Name, static pair => pair.Property,
                StringComparer.Ordinal));

            foreach (var name in properties.Select(static pair => pair.Name))
            {
                columnSet.Add(name);
            }
        }

        if (columnSet.Count == 0)
        {
            var scalarRows = rows
                .Select(static row => new[] { FormatScalar(row) })
                .ToList();

            return new ExportTable(new[] { "value" }, scalarRows);
        }

        var columns = columnSet.ToArray();
        var tableRows = new List<string[]>(rows.Count);

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var map = propertyMaps[index];
            var values = new string[columns.Length];

            for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
            {
                var column = columns[columnIndex];

                values[columnIndex] = map.TryGetValue(column, out var property)
                    ? FormatScalar(ReadProperty(row, property))
                    : string.Empty;
            }

            tableRows.Add(values);
        }

        return new ExportTable(columns, tableRows);
    }

    private static IEnumerable<(string Name, PropertyInfo Property)> GetReadableProperties(object? row)
    {
        if (row is null)
        {
            return Array.Empty<(string, PropertyInfo)>();
        }

        var type = row.GetType();

        if (IsScalarType(type))
        {
            return Array.Empty<(string, PropertyInfo)>();
        }

        return type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Select(static property => (property.Name, property))
            .OrderBy(static pair => pair.Name, StringComparer.Ordinal);
    }

    private static object? ReadProperty(object? row, PropertyInfo property)
    {
        if (row is null)
        {
            return null;
        }

        try
        {
            return property.GetValue(row);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsScalarType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        return type.IsPrimitive
               || type.IsEnum
               || type == typeof(string)
               || type == typeof(decimal)
               || type == typeof(DateTime)
               || type == typeof(DateTimeOffset)
               || type == typeof(TimeSpan)
               || type == typeof(Guid);
    }

    internal static string FormatScalar(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            string text => text,
            bool boolean => boolean ? "true" : "false",
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToBase64String(bytes),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            IEnumerable enumerable and not string => JsonSerializer.Serialize(enumerable, JsonOptions),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string EscapeCsv(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var mustQuote = value.Contains(',')
                        || value.Contains('"')
                        || value.Contains('\n')
                        || value.Contains('\r');

        if (!mustQuote)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private sealed record ExportTable(IReadOnlyList<string> Columns, IReadOnlyList<string[]> Rows);
}