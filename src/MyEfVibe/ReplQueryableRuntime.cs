using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MyEfVibe;

/// <summary>
/// Invokes <see cref="Queryable"/> and EF async operators on workspace <see cref="IQueryable{T}"/>
/// instances without compile-time casts (avoids InvalidCastException on <c>InternalDbSet&lt;T&gt;</c>).
/// </summary>
/// <remarks>
/// Public so Roslyn script submissions (separate dynamic assemblies) can call rewritten <c>db.*</c> terminals.
/// </remarks>
public static class ReplQueryableRuntime
{
    public static object First(object source) =>
        InvokeQueryable("First", source);

    public static object? FirstOrDefault(object source) =>
        InvokeQueryable("FirstOrDefault", source);

    public static object Single(object source) =>
        InvokeQueryable("Single", source);

    public static object? SingleOrDefault(object source) =>
        InvokeQueryable("SingleOrDefault", source);

    public static object First(object source, object predicate) =>
        InvokeQueryable("First", source, predicate);

    public static object? FirstOrDefault(object source, object predicate) =>
        InvokeQueryable("FirstOrDefault", source, predicate);

    public static object Single(object source, object predicate) =>
        InvokeQueryable("Single", source, predicate);

    public static object? SingleOrDefault(object source, object predicate) =>
        InvokeQueryable("SingleOrDefault", source, predicate);

    public static object Where(object source, object predicate) =>
        InvokeQueryable("Where", source, predicate);

    public static object Select(object source, object selector) =>
        InvokeQueryable("Select", source, selector);

    public static object Take(object source, object count) =>
        InvokeQueryable("Take", source, count);

    public static Task<object?> FirstAsync(object source, CancellationToken cancellationToken = default) =>
        InvokeEfAsync("FirstAsync", source, cancellationToken);

    public static Task<object?> FirstOrDefaultAsync(object source, CancellationToken cancellationToken = default) =>
        InvokeEfAsync("FirstOrDefaultAsync", source, cancellationToken);

    public static Task<object?> SingleAsync(object source, CancellationToken cancellationToken = default) =>
        InvokeEfAsync("SingleAsync", source, cancellationToken);

    public static Task<object?> SingleOrDefaultAsync(object source, CancellationToken cancellationToken = default) =>
        InvokeEfAsync("SingleOrDefaultAsync", source, cancellationToken);

    public static Task<object?> FirstAsync(object source, object predicate, CancellationToken cancellationToken = default) =>
        InvokeEfAsync("FirstAsync", source, cancellationToken, predicate);

    public static Task<object?> FirstOrDefaultAsync(object source, object predicate, CancellationToken cancellationToken = default) =>
        InvokeEfAsync("FirstOrDefaultAsync", source, cancellationToken, predicate);

    public static Task<object?> SingleAsync(object source, object predicate, CancellationToken cancellationToken = default) =>
        InvokeEfAsync("SingleAsync", source, cancellationToken, predicate);

    public static Task<object?> SingleOrDefaultAsync(object source, object predicate, CancellationToken cancellationToken = default) =>
        InvokeEfAsync("SingleOrDefaultAsync", source, cancellationToken, predicate);

    private static object InvokeQueryable(string methodName, object source, object? secondArgument = null)
    {
        var elementType = GetQueryableElementType(source);
        var parameterCount = secondArgument is null ? 1 : 2;
        var method = FindQueryableMethod(methodName, parameterCount)
            ?? throw new InvalidOperationException($"Queryable.{methodName} was not found.");

        var closed = method.MakeGenericMethod(elementType);
        var args = secondArgument is null
            ? new[] { source }
            : new[] { source, secondArgument };

        return closed.Invoke(null, args)!;
    }

    private static async Task<object?> InvokeEfAsync(
        string methodName,
        object source,
        CancellationToken cancellationToken,
        object? predicate = null)
    {
        var elementType = GetQueryableElementType(source);
        var method = FindEfAsyncMethod(methodName, predicate is not null)
            ?? throw new InvalidOperationException(
                $"EF Core extension {methodName} was not found. Ensure Microsoft.EntityFrameworkCore is referenced.");

        var closed = method.MakeGenericMethod(elementType);
        var args = BuildEfAsyncArguments(source, cancellationToken, predicate);
        var result = closed.Invoke(null, args);

        if (result is null)
            return null;

        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            return task.GetType().GetProperty("Result")?.GetValue(task);
        }

        return result;
    }

    private static object?[] BuildEfAsyncArguments(object source, CancellationToken cancellationToken, object? predicate)
    {
        if (predicate is null)
            return new object?[] { source, cancellationToken };

        return new object?[] { source, predicate, cancellationToken };
    }

    private static MethodInfo? FindQueryableMethod(string methodName, int parameterCount) =>
        typeof(Queryable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method =>
                string.Equals(method.Name, methodName, StringComparison.Ordinal)
                && method.GetParameters().Length == parameterCount
                && method.IsGenericMethodDefinition);

    private static MethodInfo? FindEfAsyncMethod(string methodName, bool hasPredicate)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.FullName?.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) != true)
                continue;

            var extensionsType = assembly.GetType(
                "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions",
                throwOnError: false);

            if (extensionsType is null)
                continue;

            foreach (var method in extensionsType.GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    continue;

                if (!method.IsDefined(typeof(ExtensionAttribute), false))
                    continue;

                var parameters = method.GetParameters();

                if (hasPredicate && parameters.Length == 3)
                    return method;

                if (!hasPredicate && parameters.Length == 2)
                    return method;
            }
        }

        return null;
    }

    private static Type GetQueryableElementType(object source)
    {
        var sourceType = source.GetType();

        foreach (var iface in sourceType.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IQueryable<>))
                return iface.GetGenericArguments()[0];
        }

        throw new InvalidOperationException(
            $"Expected an IQueryable instance but received '{sourceType.FullName}'.");
    }
}
