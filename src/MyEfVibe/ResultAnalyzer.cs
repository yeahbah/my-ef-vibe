using System.Collections;

namespace MyEfVibe;

internal static class ResultAnalyzer
{
    internal static (ResultKind Kind, string TypeName, int? RowCount, bool IsMaterialized, long? EstimatedBytes, IReadOnlyList<object?> ExportRows)
        Analyze(object? value)
    {
        if (value is null)
            return (ResultKind.Null, "null", null, true, null, Array.Empty<object?>());

        var type = value.GetType();
        var typeName = type.Name;

        if (value is string)
            return (ResultKind.String, typeName, 1, true, value.ToString()!.Length * 2L, new object?[] { value });

        if (value is System.Linq.IQueryable)
            return (ResultKind.Queryable, FormatType(type), null, false, null, Array.Empty<object?>());

        if (value is IEnumerable enumerable and not string)
        {
            var rows = new List<object?>();
            var count = 0;
            long bytes = 0;

            if (value is ICollection collection)
            {
                count = collection.Count;

                foreach (var item in collection)
                {
                    rows.Add(item);
                    bytes += EstimateSize(item);

                    if (rows.Count >= 250)
                        break;
                }
            }
            else
            {
                foreach (var item in enumerable)
                {
                    rows.Add(item);
                    count++;
                    bytes += EstimateSize(item);

                    if (count >= 250)
                        break;
                }
            }

            var kind = count == 1 && value is not IList ? ResultKind.Scalar : ResultKind.Enumerable;

            return (kind, FormatType(type), count, true, bytes, rows);
        }

        return (ResultKind.Object, FormatType(type), 1, true, EstimateSize(value), new object?[] { value });
    }

    private static string FormatType(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        var args = string.Join(", ", type.GetGenericArguments().Select(static argument => argument.Name));

        return $"{type.Name[..type.Name.IndexOf('`')]}<{args}>";
    }

    private static long EstimateSize(object? value) =>
        value?.ToString()?.Length * 2L ?? 8L;
}
