using System.Reflection;
using System.Runtime.CompilerServices;

namespace MyEfVibe;

internal static class RelationalQueryableSqlFormatter
{
    private static readonly string[] ExtensionTypeNames =
    [
        "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions",
        "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions",
    ];

    internal static bool TryGetSql(
        object? evaluatedProjection,
        IEnumerable<Assembly> inspectionAssemblies,
        out string sql)
    {
        sql = string.Empty;

        if (evaluatedProjection is null)
            return false;

        if (!typeof(System.Linq.IQueryable).IsAssignableFrom(evaluatedProjection.GetType()))
            return false;

        if (!TryInvokeToQueryString(evaluatedProjection, inspectionAssemblies, out var sqlLiteral))
            return false;

        if (string.IsNullOrWhiteSpace(sqlLiteral))
            return false;

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
            return false;

        writer.WriteLine(heading);
        writer.WriteLine(sqlLiteral);

        return true;
    }

    private static bool TryInvokeToQueryString(
        object queryable,
        IEnumerable<Assembly> inspectionAssemblies,
        out string? sqlLiteral)
    {
        sqlLiteral = null;

        foreach (var assembly in inspectionAssemblies)
        {
            foreach (var typeName in ExtensionTypeNames)
            {
                var extensionsType = assembly.GetType(typeName, throwOnError: false);

                if (extensionsType is null)
                    continue;

                foreach (var method in extensionsType.GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    if (!string.Equals(method.Name, "ToQueryString", StringComparison.Ordinal))
                        continue;

                    if (method.ReturnType != typeof(string))
                        continue;

                    if (!TryInvokeMethod(method, queryable, out sqlLiteral))
                        continue;

                    return true;
                }
            }

            foreach (var exported in ReflectionToolkit.EnumerateLoadableExportedTypes(assembly))
            foreach (var staticCandidate in exported.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!staticCandidate.IsDefined(typeof(ExtensionAttribute), inherit: false))
                    continue;

                if (!string.Equals(staticCandidate.Name, "ToQueryString", StringComparison.Ordinal))
                    continue;

                if (staticCandidate.ReturnType != typeof(string))
                    continue;

                if (!TryInvokeMethod(staticCandidate, queryable, out sqlLiteral))
                    continue;

                return true;
            }
        }

        return false;
    }

    private static bool TryInvokeMethod(MethodInfo method, object queryable, out string? sqlLiteral)
    {
        sqlLiteral = null;

        try
        {
            var candidate = method;

            if (candidate.IsGenericMethodDefinition)
            {
                var elementType = ExtractElementType(queryable.GetType());

                if (elementType is null)
                    return false;

                candidate = candidate.MakeGenericMethod(elementType);
            }

            var parameters = candidate.GetParameters();

            if (parameters.Length != 1)
                return false;

            sqlLiteral = candidate.Invoke(null, new[] { queryable }) as string;

            return !string.IsNullOrWhiteSpace(sqlLiteral);
        }
        catch
        {
            return false;
        }
    }

    private static Type? ExtractElementType(Type queryableType)
    {
        if (queryableType.IsGenericType)
            return queryableType.GetGenericArguments().FirstOrDefault();

        foreach (var iface in queryableType.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(System.Linq.IQueryable<>))
                return iface.GetGenericArguments()[0];
        }

        return null;
    }
}
