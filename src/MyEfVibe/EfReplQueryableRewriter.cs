using System.Reflection;
using System.Text.RegularExpressions;

namespace MyEfVibe;

/// <summary>
///     Rewrites REPL <c>db.*</c> queries so LINQ operators bind to <see cref="Queryable" /> / EF extensions
///     (not <see cref="Enumerable" />), so EF translates them to SQL.
/// </summary>
internal static partial class EfReplQueryableRewriter
{
    private const string Runtime = "global::MyEfVibe.ReplQueryableRuntime";

    private static readonly string[] QueryablePipelineMethods =
    [
        "OrderByDescending",
        "OrderBy",
        "ThenByDescending",
        "ThenBy",
        "Take",
        "Skip",
        "Where",
        "Select"
    ];

    [GeneratedRegex(
        @"^db\.(\w+)(.*)\.(First|FirstOrDefault|FirstAsync|FirstOrDefaultAsync)\(\)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SimpleTerminalRegex();

    [GeneratedRegex(@"^db\.(\w+)\.Take\(([^)]+)\)$", RegexOptions.CultureInvariant)]
    private static partial Regex SimpleTakeRegex();

    [GeneratedRegex(
        @"^db\.(\w+)\.Take\(([^)]+)\)\.(ToArray|ToList)\(\)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TakeThenMaterializeRegex();

    internal static string? TryRewriteToEfStaticCalls(string snippet, Type? dbContextType)
    {
        if (dbContextType is null)
        {
            return null;
        }

        var trimmed = snippet.Trim().TrimEnd(';').Trim();

        if (string.IsNullOrWhiteSpace(trimmed)
            || !LinqEfQueryHeuristics.LooksLikeEfQuery(trimmed)
            || trimmed.Contains(Runtime, StringComparison.Ordinal))
        {
            return null;
        }

        var simple = SimpleTerminalRegex().Match(trimmed);

        if (simple.Success)
        {
            var propertyName = simple.Groups[1].Value;
            var middle = simple.Groups[2].Value;
            var terminal = simple.Groups[3].Value;

            var terminalRewrite = TryBuildTerminalCall(dbContextType, propertyName, middle, terminal, null);

            if (terminalRewrite is not null)
            {
                return terminalRewrite;
            }
        }

        var terminalWithArgs = TryRewriteTerminalCallWithArguments(trimmed, dbContextType);

        if (terminalWithArgs is not null)
        {
            return terminalWithArgs;
        }

        var materializerOrAggregate = TryRewriteMaterializerOrAggregate(trimmed, dbContextType);

        if (materializerOrAggregate is not null)
        {
            return materializerOrAggregate;
        }

        var takeMaterialize = TryRewriteTakeThenMaterialize(trimmed, dbContextType);

        if (takeMaterialize is not null)
        {
            return takeMaterialize;
        }

        var deferred = TryRewriteQueryableSource(trimmed, dbContextType);

        if (deferred is not null)
        {
            return deferred;
        }

        return TryRewriteSimpleTake(trimmed);
    }

    internal static string? TryCastDbSetRoots(string snippet, Type dbContextType)
    {
        return TryRewriteQueryableSource(snippet, dbContextType)
               ?? TryRewriteTakeThenMaterialize(snippet, dbContextType)
               ?? TryRewriteSimpleTake(snippet);
    }

    /// <summary>
    ///     Rewrites <c>.Where(...).Take(n)</c> on a <c>db.Set</c> root to runtime <see cref="Queryable" /> calls.
    /// </summary>
    internal static string? TryRewriteWhereTakePipeline(string snippet, Type? dbContextType = null)
    {
        var working = snippet;
        string? materializeMethod = null;

        if (TryParseTrailingCall(working, "ToArray", out _, out var beforeToArray))
        {
            materializeMethod = nameof(ReplQueryableRuntime.ToArray);
            working = beforeToArray;
        }
        else if (TryParseTrailingCall(working, "ToList", out _, out var beforeToList))
        {
            materializeMethod = nameof(ReplQueryableRuntime.ToList);
            working = beforeToList;
        }

        if (!TryParseTrailingCall(working, "Take", out var takeArgument, out var beforeTake))
        {
            return null;
        }

        string? selectArgument = null;

        if (TryParseTrailingCall(beforeTake, "Select", out selectArgument, out var beforeSelect))
        {
            beforeTake = beforeSelect;
        }

        if (!TryParseTrailingCall(beforeTake, "Where", out var predicate, out var source))
        {
            return null;
        }

        if (!source.StartsWith("db.", StringComparison.Ordinal))
        {
            return null;
        }

        if (SourceContainsProjection(source))
        {
            var projected = $"{Runtime}.Take({beforeTake}, {takeArgument})";
            return materializeMethod is null
                ? projected
                : $"{Runtime}.{materializeMethod}({projected})";
        }

        var dbSetPropertyName = source["db.".Length..].Split('.')[0];
        TryResolveEntityTypeForDbSetProperty(dbContextType, dbSetPropertyName, out var entityType);

        var rewritten = FormatWhereCall(source, predicate, dbContextType);

        if (!string.IsNullOrWhiteSpace(selectArgument))
        {
            rewritten = FormatSelectCall(rewritten, selectArgument, dbContextType, entityType);
        }

        rewritten = $"{Runtime}.Take({rewritten}, {takeArgument})";

        return materializeMethod is null
            ? rewritten
            : $"{Runtime}.{materializeMethod}({rewritten})";
    }

    /// <summary>
    ///     Rewrites deferred <c>db.Set.Where(...)</c> probes (no terminal) so Roslyn binds <see cref="Queryable.Where" />
    ///     instead of <see cref="Enumerable.Where" />.
    /// </summary>
    internal static string? TryRewriteBareWhere(string snippet, Type? dbContextType = null)
    {
        var working = snippet.Trim().TrimEnd(';').Trim();

        if (string.IsNullOrWhiteSpace(working)
            || !LinqEfQueryHeuristics.LooksLikeEfQuery(working)
            || working.Contains(Runtime, StringComparison.Ordinal))
        {
            return null;
        }

        if (!TryParseTrailingCall(working, "Where", out var predicate, out var source))
        {
            return null;
        }

        if (!source.StartsWith("db.", StringComparison.Ordinal))
        {
            return null;
        }

        return FormatWhereCall(source, predicate, dbContextType);
    }

    private static string FormatWhereCall(string source, string predicate, Type? dbContextType)
    {
        if (TryResolveEntityTypeForDbRoot(source, dbContextType, out var entityType))
        {
            return $"{Runtime}.Where<{FormatTypeNameForScript(entityType)}>({source}, {predicate})";
        }

        return $"{Runtime}.Where({source}, {predicate})";
    }

    private static string FormatSelectCall(
        string source,
        string selector,
        Type? dbContextType,
        Type? entityType = null)
    {
        if (entityType is null
            && TryResolveEntityTypeForDbRoot(source, dbContextType, out var resolvedFromSource))
        {
            entityType = resolvedFromSource;
        }

        if (entityType is not null
            && TryGetSimpleSelectorResultType(entityType, selector, out var resultType))
        {
            return
                $"{Runtime}.Select<{FormatTypeNameForScript(entityType)}, {FormatTypeNameForScript(resultType)}>({source}, {selector})";
        }

        if (entityType is not null)
        {
            // Cast to IQueryable<T> so Roslyn binds Queryable.Select for anonymous projections
            // (CS8917 when calling ReplQueryableRuntime.Select with an inferred lambda).
            return
                $"((global::System.Linq.IQueryable<{FormatTypeNameForScript(entityType)}>)({source})).Select({selector})";
        }

        if (source.StartsWith("db.", StringComparison.Ordinal))
        {
            return $"{source}.Select({selector})";
        }

        return $"{Runtime}.Select({source}, {selector})";
    }

    private static bool TryGetSimpleSelectorResultType(Type entityType, string selector, out Type resultType)
    {
        resultType = null!;

        var arrowIndex = selector.IndexOf("=>", StringComparison.Ordinal);

        if (arrowIndex < 0)
        {
            return false;
        }

        var body = selector[(arrowIndex + 2)..].Trim().TrimEnd(')');

        if (!body.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        var memberName = body[(body.LastIndexOf('.') + 1)..];

        var property = entityType.GetProperty(
            memberName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null)
        {
            return false;
        }

        resultType = property.PropertyType;

        return true;
    }

    private static string FormatPredicateTerminalCall(
        string runtimeMethod,
        string source,
        string predicate,
        Type? dbContextType,
        string dbSetPropertyName)
    {
        if (TryResolveEntityTypeForDbSetProperty(dbContextType, dbSetPropertyName, out var entityType))
        {
            return $"{Runtime}.{runtimeMethod}<{FormatTypeNameForScript(entityType)}>({source}, {predicate})";
        }

        return $"{Runtime}.{runtimeMethod}({source}, {predicate})";
    }

    private static string? ExtractTerminalPredicate(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return null;
        }

        return SqlTranslationProbe.TryExtractPredicateArgument(arguments) is { } predicate
            ? predicate.Trim()
            : null;
    }

    private static bool TryResolveEntityTypeForDbSetProperty(
        Type? dbContextType,
        string propertyName,
        out Type entityType)
    {
        return TryResolveEntityTypeForDbRoot($"db.{propertyName}", dbContextType, out entityType);
    }

    private static string FormatTypeNameForScript(Type type)
    {
        if (string.IsNullOrEmpty(type.Namespace))
        {
            return type.Name;
        }

        return $"global::{type.FullName?.Replace('+', '.')}";
    }

    private static bool TryResolveEntityTypeForDbRoot(string source, Type? dbContextType, out Type entityType)
    {
        entityType = null!;

        if (dbContextType is null || !source.StartsWith("db.", StringComparison.Ordinal))
        {
            return false;
        }

        var afterDb = source["db.".Length..];
        var dotIndex = afterDb.IndexOf('.');

        var propertyName = (dotIndex < 0 ? afterDb : afterDb[..dotIndex]).Trim();

        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        if (!TryResolveDbSetProperty(dbContextType, propertyName))
        {
            return false;
        }

        var property = dbContextType.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property?.PropertyType is not { IsGenericType: true } propertyType)
        {
            return false;
        }

        if (propertyType.GetGenericTypeDefinition().FullName?
                .StartsWith("Microsoft.EntityFrameworkCore.DbSet`1", StringComparison.Ordinal) != true)
        {
            return false;
        }

        entityType = propertyType.GetGenericArguments()[0];

        return true;
    }

    private static string? TryRewriteTakeThenMaterialize(string snippet, Type dbContextType)
    {
        var match = TakeThenMaterializeRegex().Match(snippet.Trim().TrimEnd(';').Trim());

        if (!match.Success)
        {
            return null;
        }

        var propertyName = match.Groups[1].Value;

        if (!TryResolveDbSetProperty(dbContextType, propertyName))
        {
            return null;
        }

        var takeArgument = match.Groups[2].Value.Trim();
        var materializeMethod = match.Groups[3].Value;

        var takeCall = $"{Runtime}.Take(db.{propertyName}, {takeArgument})";

        return $"{Runtime}.{materializeMethod}({takeCall})";
    }

    private static string? TryRewriteSimpleTake(string snippet)
    {
        var match = SimpleTakeRegex().Match(snippet.Trim().TrimEnd(';').Trim());

        if (!match.Success)
        {
            return null;
        }

        return $"{Runtime}.Take(db.{match.Groups[1].Value}, {match.Groups[2].Value.Trim()})";
    }

    private static bool TryParseTrailingCall(
        string snippet,
        string methodName,
        out string argument,
        out string source)
    {
        argument = string.Empty;
        source = string.Empty;

        var suffix = $".{methodName}(";
        var callIndex = snippet.LastIndexOf(suffix, StringComparison.Ordinal);

        if (callIndex < 0)
        {
            return false;
        }

        var openParenIndex = callIndex + suffix.Length - 1;

        if (!SqlTranslationProbe.TryExtractParenthesizedContent(snippet, openParenIndex, out argument)
            || !SqlTranslationProbe.TryFindClosingParenthesis(snippet, openParenIndex, out var closeParenIndex)
            || !SqlTranslationProbe.IsEndOfExpression(snippet, closeParenIndex + 1))
        {
            return false;
        }

        source = snippet[..callIndex].TrimEnd();

        return !string.IsNullOrWhiteSpace(source);
    }

    private static string? TryRewriteTerminalCallWithArguments(string expression, Type dbContextType)
    {
        foreach (var terminal in new[]
                 {
                     "FirstOrDefaultAsync", "FirstAsync", "FirstOrDefault", "First", "SingleOrDefaultAsync",
                     "SingleAsync", "SingleOrDefault", "Single", "CountAsync", "Count"
                 })
        {
            var needle = $".{terminal}(";
            var index = expression.LastIndexOf(needle, StringComparison.Ordinal);

            if (index < 0)
            {
                continue;
            }

            var openParenIndex = index + needle.Length - 1;

            if (!SqlTranslationProbe.TryExtractParenthesizedContent(expression, openParenIndex, out var arguments))
            {
                continue;
            }

            if (!SqlTranslationProbe.IsEndOfExpression(expression, openParenIndex + arguments.Length + 2))
            {
                continue;
            }

            var source = expression[..index].TrimEnd();

            if (!source.StartsWith("db.", StringComparison.Ordinal))
            {
                continue;
            }

            var propertyName = source["db.".Length..].Split('.')[0];

            if (string.IsNullOrWhiteSpace(propertyName))
            {
                continue;
            }

            var middle = source.Length > $"db.{propertyName}".Length
                ? source[$"db.{propertyName}".Length..]
                : string.Empty;

            return TryBuildTerminalCall(dbContextType, propertyName, middle, terminal, arguments);
        }

        return null;
    }

    private static string? TryBuildTerminalCall(
        Type dbContextType,
        string propertyName,
        string middle,
        string terminal,
        string? predicate)
    {
        if (!TryResolveDbSetProperty(dbContextType, propertyName))
        {
            return null;
        }

        var source = string.IsNullOrEmpty(middle)
            ? $"db.{propertyName}"
            : $"db.{propertyName}{middle}";

        var terminalPredicate = ExtractTerminalPredicate(predicate);

        if (SourceContainsProjection(source))
        {
            return null;
        }

        source = TryRewriteQueryableSource(source, dbContextType) ?? source;

        if (terminal is "FirstAsync" or "FirstOrDefaultAsync" or "SingleAsync" or "SingleOrDefaultAsync"
            or "CountAsync")
        {
            return BuildAsyncRuntimeCall(terminal, source, terminalPredicate, dbContextType, propertyName);
        }

        var runtimeMethod = MapToRuntimeMethod(terminal);

        if (runtimeMethod is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(terminalPredicate))
        {
            return $"{Runtime}.{runtimeMethod}({source})";
        }

        return FormatPredicateTerminalCall(runtimeMethod, source, terminalPredicate, dbContextType, propertyName);
    }

    private static string? MapToRuntimeMethod(string terminal)
    {
        return terminal switch
        {
            "First" => nameof(ReplQueryableRuntime.First),
            "FirstOrDefault" => nameof(ReplQueryableRuntime.FirstOrDefault),
            "Single" => nameof(ReplQueryableRuntime.Single),
            "SingleOrDefault" => nameof(ReplQueryableRuntime.SingleOrDefault),
            "Count" => nameof(ReplQueryableRuntime.Count),
            _ => null
        };
    }

    private static string BuildAsyncRuntimeCall(
        string terminal,
        string source,
        string? predicate,
        Type dbContextType,
        string dbSetPropertyName)
    {
        var asyncMethod = terminal switch
        {
            "FirstAsync" => nameof(ReplQueryableRuntime.FirstAsync),
            "FirstOrDefaultAsync" => nameof(ReplQueryableRuntime.FirstOrDefaultAsync),
            "SingleAsync" => nameof(ReplQueryableRuntime.SingleAsync),
            "SingleOrDefaultAsync" => nameof(ReplQueryableRuntime.SingleOrDefaultAsync),
            "CountAsync" => nameof(ReplQueryableRuntime.CountAsync),
            _ => null
        };

        if (asyncMethod is null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(predicate)
            ? $"{Runtime}.{asyncMethod}({source})"
            : FormatPredicateTerminalCall(asyncMethod, source, predicate, dbContextType, dbSetPropertyName);
    }

    private static string? TryRewriteMaterializerOrAggregate(string expression, Type dbContextType)
    {
        foreach (var terminal in new[] { "ToArray", "ToList", "Count" })
        {
            if (!TryParseTrailingCall(expression, terminal, out var arguments, out var source)
                || !string.IsNullOrWhiteSpace(arguments)
                || !source.StartsWith("db.", StringComparison.Ordinal))
            {
                continue;
            }

            var rewrittenSource = TryRewriteQueryableSource(source, dbContextType) ?? source;
            var runtimeMethod = terminal switch
            {
                "ToArray" => nameof(ReplQueryableRuntime.ToArray),
                "ToList" => nameof(ReplQueryableRuntime.ToList),
                "Count" => nameof(ReplQueryableRuntime.Count),
                _ => null
            };

            if (runtimeMethod is null)
            {
                continue;
            }

            return $"{Runtime}.{runtimeMethod}({rewrittenSource})";
        }

        return null;
    }

    private static string? TryRewriteQueryableSource(string source, Type? dbContextType)
    {
        var working = EfProbeExpressionSanitizer.RemoveTranslationNeutralOperators(source.Trim());

        if (!working.StartsWith("db.", StringComparison.Ordinal))
        {
            return null;
        }

        // If the user already materialized (ToList/ToArray/etc), any subsequent operators are in-memory
        // IEnumerable LINQ. Rewriting those operators to Queryable/runtime calls will cause type failures
        // (e.g. OrderBy after ToList()).
        if (ContainsMaterializerCall(working))
        {
            return null;
        }

        var pipeline = new List<(string Method, string Argument)>();

        while (TryParseAnyTrailingPipelineCall(working, out var methodName, out var argument, out var beforeCall))
        {
            pipeline.Add((methodName, argument));
            working = beforeCall;
        }

        if (pipeline.Count == 0)
        {
            return null;
        }

        Type? entityType = null;
        string? rewritten = null;

        if (TryParseTrailingCall(working, "Where", out var wherePredicate, out var beforeWhere))
        {
            if (SourceContainsProjection(beforeWhere))
            {
                rewritten = working;
            }
            else
            {
                var dbSetPropertyName = beforeWhere["db.".Length..].Split('.')[0];
                TryResolveEntityTypeForDbSetProperty(dbContextType, dbSetPropertyName, out entityType);
                rewritten = FormatWhereCall(beforeWhere, wherePredicate, dbContextType);
            }
        }
        else if (IsDbRootOrDbRootChain(working, dbContextType, out entityType))
        {
            rewritten = working;
        }

        if (rewritten is null)
        {
            return null;
        }

        var currentElementType = entityType;

        for (var index = pipeline.Count - 1; index >= 0; index--)
        {
            var (method, argument) = pipeline[index];

            rewritten = method switch
            {
                "Where" when SourceContainsProjection(rewritten) => $"{rewritten}.Where({argument})",
                "Where" => FormatWhereCall(rewritten, argument, dbContextType),
                "Select" => ApplySelect(rewritten, argument, dbContextType, ref currentElementType),
                "Take" => $"{Runtime}.Take({rewritten}, {argument})",
                "Skip" => $"{Runtime}.Skip({rewritten}, {argument})",
                "OrderBy" => FormatOrderByCall(rewritten, argument, currentElementType, false),
                "OrderByDescending" => FormatOrderByCall(rewritten, argument, currentElementType, true),
                "ThenBy" => FormatThenByCall(rewritten, argument, dbContextType, currentElementType, false),
                "ThenByDescending" => FormatThenByCall(rewritten, argument, dbContextType, currentElementType, true),
                _ => rewritten
            };
        }

        return rewritten;
    }

    private static bool ContainsMaterializerCall(string expression)
    {
        return expression.Contains(".ToList(", StringComparison.Ordinal)
               || expression.Contains(".ToArray(", StringComparison.Ordinal)
               || expression.Contains(".AsEnumerable(", StringComparison.Ordinal)
               || expression.Contains(".AsAsyncEnumerable(", StringComparison.Ordinal);
    }

    private static string ApplySelect(
        string source,
        string selector,
        Type? dbContextType,
        ref Type? currentElementType)
    {
        var inputElementType = currentElementType;
        var rewritten = FormatSelectCall(source, selector, dbContextType, inputElementType);

        if (inputElementType is not null
            && TryGetSimpleSelectorResultType(inputElementType, selector, out var projectedType))
        {
            currentElementType = projectedType;
        }

        return rewritten;
    }

    private static bool TryParseAnyTrailingPipelineCall(
        string snippet,
        out string methodName,
        out string argument,
        out string source)
    {
        foreach (var candidate in QueryablePipelineMethods)
        {
            if (TryParseTrailingCall(snippet, candidate, out argument, out source))
            {
                methodName = candidate;

                return true;
            }
        }

        methodName = string.Empty;
        argument = string.Empty;
        source = string.Empty;

        return false;
    }

    private static string FormatOrderByCall(
        string source,
        string keySelector,
        Type? elementType,
        bool descending)
    {
        // If the source is already a non-`db.*` queryable pipeline (e.g. after `.Select(new {...})`),
        // prefer leaving the call as an IQueryable extension so Roslyn infers the correct delegate type.
        // BUT: if the source is a runtime call, it is `object`, so we must keep using runtime methods.
        if (!source.StartsWith("db.", StringComparison.Ordinal)
            && !source.StartsWith(Runtime, StringComparison.Ordinal))
        {
            var method = descending ? "OrderByDescending" : "OrderBy";
            return $"{source}.{method}({keySelector})";
        }

        var runtimeMethod = descending
            ? nameof(ReplQueryableRuntime.OrderByDescending)
            : nameof(ReplQueryableRuntime.OrderBy);

        if (elementType is not null
            && TryInferOrderByKeyType(elementType, keySelector, out var keyType))
        {
            return
                $"{Runtime}.{runtimeMethod}<{FormatTypeNameForScript(elementType)}, {FormatTypeNameForScript(keyType)}>({source}, {keySelector})";
        }

        return $"{Runtime}.{runtimeMethod}({source}, {keySelector})";
    }

    private static bool TryInferOrderByKeyType(Type elementType, string keySelector, out Type keyType)
    {
        keyType = null!;

        if (!TryGetLambdaParameterName(keySelector, out var parameterName))
        {
            return false;
        }

        var arrowIndex = keySelector.IndexOf("=>", StringComparison.Ordinal);

        if (arrowIndex < 0)
        {
            return false;
        }

        var body = keySelector[(arrowIndex + 2)..].Trim().TrimEnd(')');

        if (string.Equals(body, parameterName, StringComparison.Ordinal))
        {
            keyType = elementType;

            return true;
        }

        if (!body.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        var memberName = body[(body.LastIndexOf('.') + 1)..];

        var property = elementType.GetProperty(
            memberName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null)
        {
            return false;
        }

        keyType = property.PropertyType;

        return true;
    }

    private static bool TryGetLambdaParameterName(string selector, out string parameterName)
    {
        parameterName = string.Empty;

        var arrowIndex = selector.IndexOf("=>", StringComparison.Ordinal);

        if (arrowIndex <= 0)
        {
            return false;
        }

        var left = selector[..arrowIndex].Trim();

        if (left.StartsWith('(') && left.EndsWith(')'))
        {
            left = left[1..^1].Trim();
        }

        parameterName = left;

        return !string.IsNullOrEmpty(parameterName);
    }

    private static string FormatThenByCall(
        string source,
        string keySelector,
        Type? dbContextType,
        Type? entityType,
        bool descending)
    {
        var runtimeMethod = descending ? "ThenByDescending" : "ThenBy";

        return $"{source}.{runtimeMethod}({keySelector})";
    }

    private static bool SourceContainsProjection(string source)
    {
        return source.Contains(".Select(", StringComparison.Ordinal);
    }

    private static bool IsDbRootOrDbRootChain(string source, Type? dbContextType, out Type? entityType)
    {
        entityType = null;

        if (!source.StartsWith("db.", StringComparison.Ordinal))
        {
            return false;
        }

        var dbSetPropertyName = source["db.".Length..].Split('.')[0];

        if (string.IsNullOrWhiteSpace(dbSetPropertyName)
            || !TryResolveEntityTypeForDbSetProperty(dbContextType, dbSetPropertyName, out var resolvedEntityType))
        {
            return false;
        }

        entityType = resolvedEntityType;
        return true;
    }

    private static bool TryResolveDbSetProperty(Type dbContextType, string propertyName)
    {
        var property = dbContextType.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null || !property.PropertyType.IsGenericType)
        {
            return false;
        }

        return property.PropertyType.GetGenericTypeDefinition().FullName?
            .StartsWith("Microsoft.EntityFrameworkCore.DbSet`1", StringComparison.Ordinal) == true;
    }
}