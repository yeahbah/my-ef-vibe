using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MyEfVibe;

internal sealed record LogToBinding(MethodInfo Method, Type? LogLevelEnumType, string CommandCategory);

internal static class EntityFrameworkReflectionCache
{
    private static readonly ConcurrentDictionary<string, LogToBinding?> LogToBindings = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Assembly, MethodInfo?> ToQueryStringMethods = new();

    private static readonly string[] ExtensionTypeNames =
    [
        "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions",
        "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions",
    ];

    internal static LogToBinding? ResolveLogTo(object databaseFacade)
    {
        var key = databaseFacade.GetType().AssemblyQualifiedName ?? databaseFacade.GetType().FullName!;

        return LogToBindings.GetOrAdd(key, _ => LocateLogToBinding(databaseFacade));
    }

    internal static bool TryInvokeToQueryString(object queryable, IEnumerable<Assembly> preferredAssemblies, out string? sqlLiteral)
    {
        sqlLiteral = null;

        foreach (var assembly in OrderAssembliesForQueryable(queryable, preferredAssemblies))
        {
            var method = ToQueryStringMethods.GetOrAdd(assembly, ResolveToQueryStringMethod);

            if (method is null)
                continue;

            if (TryInvokeToQueryStringMethod(method, queryable, out sqlLiteral))
                return true;
        }

        return false;
    }

    private static IEnumerable<Assembly> OrderAssembliesForQueryable(object queryable, IEnumerable<Assembly> preferredAssemblies)
    {
        var seen = new HashSet<Assembly>();

        yield return queryable.GetType().Assembly;

        foreach (var assembly in preferredAssemblies)
        {
            if (seen.Add(assembly))
                yield return assembly;
        }
    }

    private static MethodInfo? ResolveToQueryStringMethod(Assembly assembly)
    {
        if (assembly.FullName?.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) != true)
            return null;

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

                if (method.GetParameters().Length == 1)
                    return method;
            }
        }

        foreach (var exported in ReflectionToolkit.EnumerateLoadableExportedTypes(assembly))
        {
            foreach (var candidate in exported.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!candidate.IsDefined(typeof(ExtensionAttribute), inherit: false))
                    continue;

                if (!string.Equals(candidate.Name, "ToQueryString", StringComparison.Ordinal))
                    continue;

                if (candidate.ReturnType != typeof(string))
                    continue;

                if (candidate.GetParameters().Length == 1)
                    return candidate;
            }
        }

        return null;
    }

    private static bool TryInvokeToQueryStringMethod(MethodInfo method, object queryable, out string? sqlLiteral)
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

            if (candidate.GetParameters().Length != 1)
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

    private static LogToBinding? LocateLogToBinding(object databaseFacade)
    {
        const string commandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.FullName?.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) != true)
                continue;

            foreach (var exported in ReflectionToolkit.EnumerateLoadableExportedTypes(assembly))
            foreach (var candidate in exported.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!candidate.IsDefined(typeof(ExtensionAttribute), inherit: false))
                    continue;

                if (!string.Equals(candidate.Name, "LogTo", StringComparison.Ordinal))
                    continue;

                var parameters = candidate.GetParameters();

                if (parameters.Length < 2)
                    continue;

                if (!parameters[0].ParameterType.IsAssignableFrom(databaseFacade.GetType()))
                    continue;

                Type? logLevelEnumType = null;

                if (parameters.Length >= 3 && parameters[2].ParameterType.IsEnum)
                    logLevelEnumType = parameters[2].ParameterType;

                if (parameters[1].ParameterType == typeof(Action<string>))
                    return new LogToBinding(candidate, logLevelEnumType, commandCategory);

                if (parameters[1].ParameterType.IsGenericType
                    && parameters[1].ParameterType.GetGenericTypeDefinition() == typeof(Action<>))
                {
                    var actionGenericArgs = parameters[1].ParameterType.GetGenericArguments();

                    if (actionGenericArgs.Length == 1
                        && string.Equals(actionGenericArgs[0].Name, "DbCommand", StringComparison.Ordinal))
                    {
                        return new LogToBinding(candidate, logLevelEnumType, commandCategory);
                    }

                    if (actionGenericArgs.Length == 4
                        && actionGenericArgs[3] == typeof(string))
                    {
                        return new LogToBinding(candidate, logLevelEnumType, commandCategory);
                    }
                }
            }
        }

        return null;
    }
}
