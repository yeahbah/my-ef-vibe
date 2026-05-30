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

    public static object First<T>(object source, Expression<Func<T, bool>> predicate) =>
        Queryable.First((IQueryable<T>)source, predicate)!;

    public static object? FirstOrDefault<T>(object source, Expression<Func<T, bool>> predicate) =>
        Queryable.FirstOrDefault((IQueryable<T>)source, predicate);

    public static object Single<T>(object source, Expression<Func<T, bool>> predicate) =>
        Queryable.Single((IQueryable<T>)source, predicate)!;

    public static object? SingleOrDefault<T>(object source, Expression<Func<T, bool>> predicate) =>
        Queryable.SingleOrDefault((IQueryable<T>)source, predicate);

    public static object Where(object source, object predicate) =>
        InvokeQueryable("Where", source, predicate);

    /// <summary>
    /// Typed predicate for script compilation; <paramref name="source"/> stays <see cref="object"/>
    /// so <c>DbSet&lt;T&gt;</c> from the REPL host does not need to match metadata <c>T</c> at compile time.
    /// </summary>
    public static object Where<T>(object source, Expression<Func<T, bool>> predicate) =>
        InvokeQueryable("Where", source, predicate);

    public static object Select<TSource, TResult>(object source, Expression<Func<TSource, TResult>> selector)
    {
        var method = typeof(Queryable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(method =>
                string.Equals(method.Name, "Select", StringComparison.Ordinal)
                && method.IsGenericMethodDefinition
                && method.GetGenericArguments().Length == 2
                && method.GetParameters().Length == 2);

        return method.MakeGenericMethod(typeof(TSource), typeof(TResult)).Invoke(null, [source, selector])!;
    }

    /// <summary>
    /// Projection after a runtime <see cref="IQueryable"/> step (e.g. <see cref="Take"/>); infers
    /// <c>TResult</c> from the expression tree (supports anonymous types).
    /// </summary>
    public static object Select(object source, Expression selector) =>
        InvokeQueryableSelect(source, selector);

    public static object Take(object source, object count) =>
        InvokeQueryable("Take", source, count);

    public static object Skip(object source, object count) =>
        InvokeQueryable("Skip", source, count);

    public static object OrderBy(object source, Expression keySelector) =>
        InvokeQueryableOrderBy("OrderBy", source, keySelector);

    public static object OrderBy<TSource, TKey>(object source, Expression<Func<TSource, TKey>> keySelector) =>
        InvokeQueryableOrderBy("OrderBy", source, keySelector);

    public static object OrderByDescending(object source, Expression keySelector) =>
        InvokeQueryableOrderBy("OrderByDescending", source, keySelector);

    public static object OrderByDescending<TSource, TKey>(object source, Expression<Func<TSource, TKey>> keySelector) =>
        InvokeQueryableOrderBy("OrderByDescending", source, keySelector);

    public static object Count(object source) =>
        InvokeQueryable("Count", source);

    public static object Count<T>(object source, Expression<Func<T, bool>> predicate) =>
        InvokeQueryable("Count", source, predicate);

    public static object ToArray(object source) =>
        InvokeEnumerable("ToArray", source);

    public static object ToList(object source) =>
        InvokeEnumerable("ToList", source);

    public static Task<object?> FirstAsync(object source, CancellationToken cancellationToken = default) =>
        InvokeEfAsync("FirstAsync", source, cancellationToken);

    public static Task<object?> FirstOrDefaultAsync(object source, CancellationToken cancellationToken = default) =>
        InvokeEfAsync("FirstOrDefaultAsync", source, cancellationToken);

    public static Task<object?> SingleAsync(object source, CancellationToken cancellationToken = default) =>
        InvokeEfAsync("SingleAsync", source, cancellationToken);

    public static Task<object?> SingleOrDefaultAsync(object source, CancellationToken cancellationToken = default) =>
        InvokeEfAsync("SingleOrDefaultAsync", source, cancellationToken);

    public static Task<object?> FirstAsync<T>(object source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        InvokeEfAsync("FirstAsync", source, cancellationToken, predicate);

    public static Task<object?> FirstOrDefaultAsync<T>(object source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        InvokeEfAsync("FirstOrDefaultAsync", source, cancellationToken, predicate);

    public static Task<object?> SingleAsync<T>(object source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        InvokeEfAsync("SingleAsync", source, cancellationToken, predicate);

    public static Task<object?> SingleOrDefaultAsync<T>(object source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        InvokeEfAsync("SingleOrDefaultAsync", source, cancellationToken, predicate);

    public static Task<object?> CountAsync(object source, CancellationToken cancellationToken = default) =>
        InvokeEfAsync("CountAsync", source, cancellationToken);

    public static Task<object?> CountAsync<T>(object source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        InvokeEfAsync("CountAsync", source, cancellationToken, predicate);

    private static object InvokeQueryableSelect(object source, Expression selector)
    {
        if (selector is not LambdaExpression lambda)
        {
            throw new InvalidOperationException(
                $"Expected a lambda expression for Select but received '{selector.NodeType}'.");
        }

        var sourceType = GetQueryableElementType(source);
        var method = FindQueryableMethod("Select", 2)
            ?? throw new InvalidOperationException("Queryable.Select was not found.");

        return method.MakeGenericMethod(sourceType, lambda.ReturnType).Invoke(null, [source, selector])!;
    }

    private static object InvokeQueryableOrderBy(string methodName, object source, Expression keySelector)
    {
        if (keySelector is not LambdaExpression lambda)
        {
            throw new InvalidOperationException(
                $"Expected a lambda expression for {methodName} but received '{keySelector.NodeType}'.");
        }

        return InvokeQueryableOrderBy(methodName, source, lambda);
    }

    private static object InvokeQueryableOrderBy(string methodName, object source, LambdaExpression keySelector)
    {
        var sourceType = GetQueryableElementType(source);
        var method = FindQueryableMethod(methodName, 2)
            ?? throw new InvalidOperationException($"Queryable.{methodName} was not found.");

        return method.MakeGenericMethod(sourceType, keySelector.ReturnType).Invoke(null, [source, keySelector])!;
    }

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

    private static object InvokeEnumerable(string methodName, object source)
    {
        var elementType = GetQueryableElementType(source);
        var method = FindEnumerableMethod(methodName)
            ?? throw new InvalidOperationException($"Enumerable.{methodName} was not found.");

        return method.MakeGenericMethod(elementType).Invoke(null, [source])!;
    }

    private static MethodInfo? FindQueryableMethod(string methodName, int parameterCount) =>
        typeof(Queryable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method =>
                string.Equals(method.Name, methodName, StringComparison.Ordinal)
                && method.GetParameters().Length == parameterCount
                && method.IsGenericMethodDefinition);

    private static MethodInfo? FindEnumerableMethod(string methodName) =>
        typeof(Enumerable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method =>
                string.Equals(method.Name, methodName, StringComparison.Ordinal)
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 1);

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
