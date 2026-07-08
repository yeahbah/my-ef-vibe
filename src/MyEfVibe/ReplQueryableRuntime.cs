using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MyEfVibe;

/// <summary>
///     Invokes <see cref="Queryable" /> and EF async operators on workspace <see cref="IQueryable{T}" />
///     instances without compile-time casts (avoids InvalidCastException on <c>InternalDbSet&lt;T&gt;</c>).
/// </summary>
/// <remarks>
///     Public so Roslyn script submissions (separate dynamic assemblies) can call rewritten <c>db.*</c> terminals.
/// </remarks>
public static class ReplQueryableRuntime
{
    public static object First(object source)
    {
        return InvokeQueryable("First", source);
    }

    public static object? FirstOrDefault(object source)
    {
        return InvokeQueryable("FirstOrDefault", source);
    }

    public static object Single(object source)
    {
        return InvokeQueryable("Single", source);
    }

    public static object? SingleOrDefault(object source)
    {
        return InvokeQueryable("SingleOrDefault", source);
    }

    public static object First<T>(object source, Expression<Func<T, bool>> predicate)
    {
        return ((IQueryable<T>)source).First(predicate)!;
    }

    public static object? FirstOrDefault<T>(object source, Expression<Func<T, bool>> predicate)
    {
        return ((IQueryable<T>)source).FirstOrDefault(predicate);
    }

    public static object Single<T>(object source, Expression<Func<T, bool>> predicate)
    {
        return ((IQueryable<T>)source).Single(predicate)!;
    }

    public static object? SingleOrDefault<T>(object source, Expression<Func<T, bool>> predicate)
    {
        return ((IQueryable<T>)source).SingleOrDefault(predicate);
    }

    public static object Where(object source, object predicate)
    {
        return InvokeQueryable("Where", source, predicate);
    }

    /// <summary>
    ///     Typed predicate for script compilation; <paramref name="source" /> stays <see cref="object" />
    ///     so <c>DbSet&lt;T&gt;</c> from the REPL host does not need to match metadata <c>T</c> at compile time.
    /// </summary>
    public static object Where<T>(object source, Expression<Func<T, bool>> predicate)
    {
        return InvokeQueryable("Where", source, predicate);
    }

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
    ///     Projection after a runtime <see cref="IQueryable" /> step (e.g. <see cref="Take" />); infers
    ///     <c>TResult</c> from the expression tree (supports anonymous types).
    /// </summary>
    public static object Select(object source, Expression selector)
    {
        return InvokeQueryableSelect(source, selector);
    }

    public static object Take(object source, object count)
    {
        return InvokeQueryable("Take", source, count);
    }

    public static object Skip(object source, object count)
    {
        return InvokeQueryable("Skip", source, count);
    }

    public static object OrderBy(object source, Expression keySelector)
    {
        return InvokeQueryableOrderBy("OrderBy", source, keySelector);
    }

    public static object OrderBy<TSource, TKey>(object source, Expression<Func<TSource, TKey>> keySelector)
    {
        return InvokeQueryableOrderBy("OrderBy", source, keySelector);
    }

    public static object OrderByDescending(object source, Expression keySelector)
    {
        return InvokeQueryableOrderBy("OrderByDescending", source, keySelector);
    }

    public static object OrderByDescending<TSource, TKey>(object source, Expression<Func<TSource, TKey>> keySelector)
    {
        return InvokeQueryableOrderBy("OrderByDescending", source, keySelector);
    }

    public static object Count(object source)
    {
        return InvokeQueryable("Count", source);
    }

    public static object Count<T>(object source, Expression<Func<T, bool>> predicate)
    {
        return InvokeQueryable("Count", source, predicate);
    }

    public static object ToArray(object source)
    {
        return InvokeEnumerable("ToArray", source);
    }

    public static object ToList(object source)
    {
        return InvokeEnumerable("ToList", source);
    }

    public static Task<object?> FirstAsync(object source, CancellationToken cancellationToken = default)
    {
        return InvokeEfAsync("FirstAsync", source, cancellationToken);
    }

    public static Task<object?> FirstOrDefaultAsync(object source, CancellationToken cancellationToken = default)
    {
        return InvokeEfAsync("FirstOrDefaultAsync", source, cancellationToken);
    }

    public static Task<object?> SingleAsync(object source, CancellationToken cancellationToken = default)
    {
        return InvokeEfAsync("SingleAsync", source, cancellationToken);
    }

    public static Task<object?> SingleOrDefaultAsync(object source, CancellationToken cancellationToken = default)
    {
        return InvokeEfAsync("SingleOrDefaultAsync", source, cancellationToken);
    }

    public static Task<object?> FirstAsync<T>(object source, Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return InvokeEfAsync("FirstAsync", source, cancellationToken, predicate);
    }

    public static Task<object?> FirstOrDefaultAsync<T>(object source, Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return InvokeEfAsync("FirstOrDefaultAsync", source, cancellationToken, predicate);
    }

    public static Task<object?> SingleAsync<T>(object source, Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return InvokeEfAsync("SingleAsync", source, cancellationToken, predicate);
    }

    public static Task<object?> SingleOrDefaultAsync<T>(object source, Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return InvokeEfAsync("SingleOrDefaultAsync", source, cancellationToken, predicate);
    }

    public static Task<object?> CountAsync(object source, CancellationToken cancellationToken = default)
    {
        return InvokeEfAsync("CountAsync", source, cancellationToken);
    }

    public static Task<object?> CountAsync<T>(object source, Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return InvokeEfAsync("CountAsync", source, cancellationToken, predicate);
    }

    public static Task<object?> ToListAsync(object source, CancellationToken cancellationToken = default)
    {
        return InvokeEfAsync("ToListAsync", source, cancellationToken);
    }

    public static Task<object?> ToArrayAsync(object source, CancellationToken cancellationToken = default)
    {
        return InvokeEfAsync("ToArrayAsync", source, cancellationToken);
    }

    /// <summary>
    ///     Couchbase EF returns scalar projections as JSON objects; unwrap anonymous projection rows
    ///     (e.g. <c>new { Name = x.Name }</c>) back to a typed array of the scalar member.
    /// </summary>
    public static object UnwrapScalarProjection(object materialized, string memberName)
    {
        if (materialized is not System.Collections.IEnumerable enumerable)
        {
            return materialized;
        }

        var items = new List<object?>();

        foreach (var item in enumerable)
        {
            items.Add(item);
        }

        if (items.Count == 0)
        {
            return Array.Empty<string>();
        }

        var property = items[0]?.GetType().GetProperty(
            memberName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null)
        {
            return materialized;
        }

        var elementType = property.PropertyType;
        var array = Array.CreateInstance(elementType, items.Count);

        for (var index = 0; index < items.Count; index++)
        {
            var value = items[index] is null ? null : property.GetValue(items[index]);
            array.SetValue(value, index);
        }

        return array;
    }

    public static async Task<object?> UnwrapScalarProjectionAsync(
        Task<object?> materializedTask,
        string memberName,
        CancellationToken cancellationToken = default)
    {
        if (materializedTask is null)
        {
            return null;
        }

        _ = cancellationToken;

        var materialized = await materializedTask.ConfigureAwait(false);

        if (materialized is null)
        {
            return null;
        }

        return UnwrapScalarProjection(materialized, memberName);
    }

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
        {
            return null;
        }

        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            return task.GetType().GetProperty("Result")?.GetValue(task);
        }

        return result;
    }

    private static object?[] BuildEfAsyncArguments(object source, CancellationToken cancellationToken,
        object? predicate)
    {
        if (predicate is null)
        {
            return [source, cancellationToken];
        }

        return [source, predicate, cancellationToken];
    }

    private static object InvokeEnumerable(string methodName, object source)
    {
        var elementType = GetQueryableElementType(source);
        var method = FindEnumerableMethod(methodName)
                     ?? throw new InvalidOperationException($"Enumerable.{methodName} was not found.");

        return method.MakeGenericMethod(elementType).Invoke(null, [source])!;
    }

    private static MethodInfo? FindQueryableMethod(string methodName, int parameterCount)
    {
        return typeof(Queryable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method =>
                string.Equals(method.Name, methodName, StringComparison.Ordinal)
                && method.GetParameters().Length == parameterCount
                && method.IsGenericMethodDefinition);
    }

    private static MethodInfo? FindEnumerableMethod(string methodName)
    {
        return typeof(Enumerable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method =>
                string.Equals(method.Name, methodName, StringComparison.Ordinal)
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 1);
    }

    private static MethodInfo? FindEfAsyncMethod(string methodName, bool hasPredicate)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.FullName?.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) != true)
            {
                continue;
            }

            var extensionsType = assembly.GetType(
                "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions",
                false);

            if (extensionsType is null)
            {
                continue;
            }

            foreach (var method in extensionsType.GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!method.IsDefined(typeof(ExtensionAttribute), false))
                {
                    continue;
                }

                var parameters = method.GetParameters();

                if (hasPredicate && parameters.Length == 3)
                {
                    return method;
                }

                if (!hasPredicate && parameters.Length == 2)
                {
                    return method;
                }
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
            {
                return iface.GetGenericArguments()[0];
            }
        }

        throw new InvalidOperationException(
            $"Expected an IQueryable instance but received '{sourceType.FullName}'.");
    }
}