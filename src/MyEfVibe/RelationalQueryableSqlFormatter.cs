using System.Reflection;

namespace MyEfVibe;

internal static class RelationalQueryableSqlFormatter
{
    internal static bool TryGetSql(
        object? evaluatedProjection,
        IEnumerable<Assembly> inspectionAssemblies,
        out string sql)
    {
        sql = string.Empty;

        if (evaluatedProjection is null)
        {
            return false;
        }

        if (!typeof(IQueryable).IsAssignableFrom(evaluatedProjection.GetType()))
        {
            return false;
        }

        if (!EntityFrameworkReflectionCache.TryInvokeToQueryString(
                evaluatedProjection,
                inspectionAssemblies,
                out var sqlLiteral))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(sqlLiteral))
        {
            return false;
        }

        sql = sqlLiteral;
        return true;
    }

    internal static bool TryWrite(
        object? evaluatedProjection,
        TextWriter writer,
        IEnumerable<Assembly> inspectionAssemblies,
        string heading = "Translated SQL:")
    {
        if (!TryGetSql(evaluatedProjection, inspectionAssemblies, out var sqlLiteral))
        {
            return false;
        }

        writer.WriteLine(heading);
        writer.WriteLine(sqlLiteral);

        return true;
    }
}