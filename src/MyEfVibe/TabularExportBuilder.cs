using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyEfVibe;

internal static class TabularExportBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

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
        var columns = new List<string>();
        var columnSet = new HashSet<string>(StringComparer.Ordinal);
        var propertyMaps = new List<IReadOnlyDictionary<string, PropertyInfo>>();

        foreach (var row in rows)
        {
            var properties = GetReadableProperties(row).ToList();

            propertyMaps.Add(properties.ToDictionary(static pair => pair.Name, static pair => pair.Property,
                StringComparer.Ordinal));

            foreach (var name in properties.Select(static pair => pair.Name))
            {
                if (columnSet.Add(name))
                {
                    columns.Add(name);
                }
            }
        }

        if (columns.Count == 0)
        {
            var scalarRows = rows
                .Select(static row => new[] { FormatScalar(row) })
                .ToList();

            return new ExportTable(["value"], scalarRows);
        }

        var tableRows = new List<string[]>(rows.Count);

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var map = propertyMaps[index];
            var values = new string[columns.Count];

            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
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
            return [];
        }

        var type = row.GetType();

        if (IsScalarType(type))
        {
            return [];
        }

        return type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Where(static property => IsExportableProperty(property))
            .Select(static property => (property.Name, property));
    }

    private static bool IsExportableProperty(PropertyInfo property)
    {
        return IsExportablePropertyType(property.PropertyType);
    }

    private static bool IsExportablePropertyType(Type propertyType)
    {
        propertyType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (IsScalarType(propertyType) || propertyType.IsEnum || propertyType.IsValueType)
        {
            return true;
        }

        if (TryGetEnumerableElementType(propertyType, out var elementType))
        {
            elementType = Nullable.GetUnderlyingType(elementType) ?? elementType;

            return IsScalarType(elementType) || elementType.IsEnum;
        }

        return false;
    }

    private static bool TryGetEnumerableElementType(Type type, out Type elementType)
    {
        elementType = typeof(object);

        if (type == typeof(string))
        {
            return false;
        }

        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();

            if (definition == typeof(IEnumerable<>)
                || definition == typeof(IList<>)
                || definition == typeof(ICollection<>)
                || definition == typeof(List<>)
                || definition == typeof(IReadOnlyList<>)
                || definition == typeof(IReadOnlyCollection<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }
        }

        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                elementType = iface.GetGenericArguments()[0];
                return true;
            }
        }

        return false;
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
            IEnumerable enumerable and not string => FormatEnumerable(enumerable),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string FormatEnumerable(IEnumerable enumerable)
    {
        var values = new List<string>();

        foreach (var item in enumerable)
        {
            values.Add(item switch
            {
                null => string.Empty,
                string text => text,
                _ when IsScalarType(item.GetType()) || item.GetType().IsEnum =>
                    FormatScalar(item),
                _ => item.ToString() ?? string.Empty,
            });

            if (values.Count >= 32)
            {
                values.Add("…");
                break;
            }
        }

        return values.Count == 0 ? "(empty)" : $"[{string.Join(", ", values)}]";
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