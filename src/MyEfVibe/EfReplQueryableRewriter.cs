using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MyEfVibe.Linq;

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
        return TryRewriteToEfStaticCalls(snippet, dbContextType, EfReplQueryRewriteOptions.Sync);
    }

    internal static string? TryRewriteToEfStaticCalls(
        string snippet,
        Type? dbContextType,
        EfReplQueryRewriteOptions options)
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

        var embedded = TryRewriteEmbeddedTerminalCalls(trimmed, dbContextType, options);

        if (embedded is not null)
        {
            return ApplyAsyncAwait(embedded, options);
        }

        var simple = SimpleTerminalRegex().Match(trimmed);

        if (simple.Success)
        {
            var propertyName = simple.Groups[1].Value;
            var middle = simple.Groups[2].Value;
            var terminal = simple.Groups[3].Value;

            var terminalRewrite = TryBuildTerminalCall(dbContextType, propertyName, middle, terminal, null, options);

            if (terminalRewrite is not null)
            {
                return ApplyAsyncAwait(terminalRewrite, options);
            }
        }

        var terminalWithArgs = TryRewriteTerminalCallWithArguments(trimmed, dbContextType, options);

        if (terminalWithArgs is not null)
        {
            return ApplyAsyncAwait(terminalWithArgs, options);
        }

        var materializerOrAggregate = TryRewriteMaterializerOrAggregate(trimmed, dbContextType, options);

        if (materializerOrAggregate is not null)
        {
            return ApplyAsyncAwait(materializerOrAggregate, options);
        }

        var takeMaterialize = TryRewriteTakeThenMaterialize(trimmed, dbContextType, options);

        if (takeMaterialize is not null)
        {
            return ApplyAsyncAwait(takeMaterialize, options);
        }

        var deferred = TryRewriteQueryableSource(trimmed, dbContextType, options);

        if (deferred is not null)
        {
            return deferred;
        }

        return TryRewriteSimpleTake(trimmed);
    }

    internal static string? TryCastDbSetRoots(string snippet, Type dbContextType)
    {
        return TryCastDbSetRoots(snippet, dbContextType, EfReplQueryRewriteOptions.Sync);
    }

    internal static string? TryCastDbSetRoots(
        string snippet,
        Type dbContextType,
        EfReplQueryRewriteOptions options)
    {
        return TryRewriteQueryableSource(snippet, dbContextType, options)
               ?? TryRewriteTakeThenMaterialize(snippet, dbContextType, options)
               ?? TryRewriteSimpleTake(snippet);
    }

    /// <summary>
    ///     Rewrites <c>.Where(...).Take(n)</c> on a <c>db.Set</c> root to runtime <see cref="Queryable" /> calls.
    /// </summary>
    internal static string? TryRewriteWhereTakePipeline(string snippet, Type? dbContextType = null)
    {
        return TryRewriteWhereTakePipeline(snippet, dbContextType, EfReplQueryRewriteOptions.Sync);
    }

    internal static string? TryRewriteWhereTakePipeline(
        string snippet,
        Type? dbContextType,
        EfReplQueryRewriteOptions options)
    {
        var working = snippet;
        string? materializeMethod = null;

        if (TryParseTrailingCall(working, "ToArray", out _, out var beforeToArray))
        {
            materializeMethod = options.PreferAsyncQueries
                ? nameof(ReplQueryableRuntime.ToArrayAsync)
                : nameof(ReplQueryableRuntime.ToArray);
            working = beforeToArray;
        }
        else if (TryParseTrailingCall(working, "ToList", out _, out var beforeToList))
        {
            materializeMethod = options.PreferAsyncQueries
                ? nameof(ReplQueryableRuntime.ToListAsync)
                : nameof(ReplQueryableRuntime.ToList);
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

            if (materializeMethod is null)
            {
                return projected;
            }

            return ApplyAsyncAwait($"{Runtime}.{materializeMethod}({projected})", options);
        }

        var dbSetPropertyName = source["db.".Length..].Split('.')[0];
        TryResolveEntityTypeForDbSetProperty(dbContextType, dbSetPropertyName, out var entityType);

        var rewritten = FormatWhereCall(source, predicate, dbContextType);

        if (!string.IsNullOrWhiteSpace(selectArgument))
        {
            rewritten = FormatSelectCall(rewritten, selectArgument, dbContextType, entityType, options);
        }

        rewritten = $"{Runtime}.Take({rewritten}, {takeArgument})";

        if (materializeMethod is null)
        {
            return rewritten;
        }

        var materialized = $"{Runtime}.{materializeMethod}({rewritten})";

        if (options.PreferAsyncQueries
            && materializeMethod is nameof(ReplQueryableRuntime.ToArrayAsync) or nameof(ReplQueryableRuntime.ToListAsync)
            && TryExtractCouchbaseScalarProjectionMember(rewritten, out var memberName))
        {
            materialized = $"{Runtime}.UnwrapScalarProjectionAsync({materialized}, \"{memberName}\")";
        }

        return ApplyAsyncAwait(materialized, options);
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
        Type? entityType = null,
        EfReplQueryRewriteOptions options = default)
    {
        if (entityType is null
            && TryResolveEntityTypeForDbRoot(source, dbContextType, out var resolvedFromSource))
        {
            entityType = resolvedFromSource;
        }

        if (entityType is not null
            && TryGetSimpleSelectorResultType(entityType, selector, out var resultType))
        {
            if (options.PreferAsyncQueries
                && IsScalarProjectionType(resultType)
                && TryGetScalarMemberName(selector, out var memberName))
            {
                return
                    $"((global::System.Linq.IQueryable<{FormatTypeNameForScript(entityType)}>)({source})).Select({FormatScalarWrappedSelector(selector, memberName)})";
            }

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

        return TryGetScalarMemberName(selector, out var memberName)
               && TryResolveScalarMemberType(entityType, memberName, out resultType);
    }

    private static bool TryGetScalarMemberName(string selector, out string memberName)
    {
        memberName = string.Empty;

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

        memberName = body[(body.LastIndexOf('.') + 1)..];

        return !string.IsNullOrWhiteSpace(memberName);
    }

    private static bool TryResolveScalarMemberType(Type entityType, string memberName, out Type resultType)
    {
        resultType = null!;

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

    private static bool IsScalarProjectionType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.IsPrimitive
               || underlying == typeof(string)
               || underlying == typeof(decimal)
               || underlying == typeof(DateTime)
               || underlying == typeof(DateTimeOffset)
               || underlying == typeof(Guid)
               || underlying == typeof(TimeSpan);
    }

    private static string FormatScalarWrappedSelector(string selector, string memberName)
    {
        var arrowIndex = selector.IndexOf("=>", StringComparison.Ordinal);
        var parameter = selector[..arrowIndex].Trim();
        var body = selector[(arrowIndex + 2)..].Trim().TrimEnd(')');

        return $"{parameter} => new {{ {memberName} = {body} }}";
    }

    private static bool TryExtractCouchbaseScalarProjectionMember(string source, out string memberName)
    {
        memberName = string.Empty;

        var match = CouchbaseScalarProjectionRegex().Match(source);

        if (!match.Success)
        {
            return false;
        }

        memberName = match.Groups["member"].Value;

        return !string.IsNullOrWhiteSpace(memberName);
    }

    [GeneratedRegex(
        @"\.Select\(\s*(?<param>\w+)\s*=>\s*new\s*\{\s*(?<member>\w+)\s*=\s*(?<param>\w+)\.(?<member>\w+)\s*\}\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex CouchbaseScalarProjectionRegex();

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

    private static string? TryRewriteTakeThenMaterialize(
        string snippet,
        Type dbContextType,
        EfReplQueryRewriteOptions options = default)
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

        if (options.PreferAsyncQueries)
        {
            materializeMethod = materializeMethod switch
            {
                "ToList" => nameof(ReplQueryableRuntime.ToListAsync),
                "ToArray" => nameof(ReplQueryableRuntime.ToArrayAsync),
                _ => materializeMethod
            };
        }
        else
        {
            materializeMethod = materializeMethod switch
            {
                "ToList" => nameof(ReplQueryableRuntime.ToList),
                "ToArray" => nameof(ReplQueryableRuntime.ToArray),
                _ => materializeMethod
            };
        }

        var takeCall = $"{Runtime}.Take(db.{propertyName}, {takeArgument})";

        return ApplyAsyncAwait($"{Runtime}.{materializeMethod}({takeCall})", options);
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

    private static string? TryRewriteTerminalCallWithArguments(
        string expression,
        Type dbContextType,
        EfReplQueryRewriteOptions options = default)
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

            return TryBuildTerminalCall(dbContextType, propertyName, middle, terminal, arguments, options);
        }

        return null;
    }

    private static string? TryBuildTerminalCall(
        Type dbContextType,
        string propertyName,
        string middle,
        string terminal,
        string? predicate,
        EfReplQueryRewriteOptions options = default)
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

        source = TryRewriteQueryableSource(source, dbContextType, options) ?? source;

        if (options.PreferAsyncQueries
            && TryMapSyncTerminalToAsync(terminal, out var asyncTerminal))
        {
            return BuildAsyncRuntimeCall(asyncTerminal, source, terminalPredicate, dbContextType, propertyName);
        }

        if (terminal is "FirstAsync" or "FirstOrDefaultAsync" or "SingleAsync" or "SingleOrDefaultAsync"
            or "CountAsync")
        {
            return BuildAsyncRuntimeCall(terminal, source, terminalPredicate, dbContextType, propertyName);
        }

        var runtimeMethod = MapToRuntimeMethod(terminal, options);

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

    private static string? MapToRuntimeMethod(string terminal, EfReplQueryRewriteOptions options = default)
    {
        if (options.PreferAsyncQueries && TryMapSyncTerminalToAsync(terminal, out var asyncTerminal))
        {
            return asyncTerminal switch
            {
                "FirstAsync" => nameof(ReplQueryableRuntime.FirstAsync),
                "FirstOrDefaultAsync" => nameof(ReplQueryableRuntime.FirstOrDefaultAsync),
                "SingleAsync" => nameof(ReplQueryableRuntime.SingleAsync),
                "SingleOrDefaultAsync" => nameof(ReplQueryableRuntime.SingleOrDefaultAsync),
                "CountAsync" => nameof(ReplQueryableRuntime.CountAsync),
                _ => null
            };
        }

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

    private static bool TryMapSyncTerminalToAsync(string terminal, out string asyncTerminal)
    {
        asyncTerminal = terminal switch
        {
            "First" => "FirstAsync",
            "FirstOrDefault" => "FirstOrDefaultAsync",
            "Single" => "SingleAsync",
            "SingleOrDefault" => "SingleOrDefaultAsync",
            "Count" => "CountAsync",
            _ => string.Empty
        };

        return asyncTerminal.Length > 0;
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

    private static string? TryRewriteMaterializerOrAggregate(
        string expression,
        Type dbContextType,
        EfReplQueryRewriteOptions options = default)
    {
        foreach (var terminal in new[] { "ToArray", "ToList", "Count" })
        {
            if (!TryParseTrailingCall(expression, terminal, out var arguments, out var source)
                || !string.IsNullOrWhiteSpace(arguments)
                || !source.StartsWith("db.", StringComparison.Ordinal))
            {
                continue;
            }

            var rewrittenSource = TryRewriteQueryableSource(source, dbContextType, options) ?? source;
            var runtimeMethod = terminal switch
            {
                "ToArray" when options.PreferAsyncQueries => nameof(ReplQueryableRuntime.ToArrayAsync),
                "ToArray" => nameof(ReplQueryableRuntime.ToArray),
                "ToList" when options.PreferAsyncQueries => nameof(ReplQueryableRuntime.ToListAsync),
                "ToList" => nameof(ReplQueryableRuntime.ToList),
                "Count" when options.PreferAsyncQueries => nameof(ReplQueryableRuntime.CountAsync),
                "Count" => nameof(ReplQueryableRuntime.Count),
                _ => null
            };

            if (runtimeMethod is null)
            {
                continue;
            }

            var materializeCall = $"{Runtime}.{runtimeMethod}({rewrittenSource})";

            if (options.PreferAsyncQueries
                && terminal is "ToArray" or "ToList"
                && TryExtractCouchbaseScalarProjectionMember(rewrittenSource, out var memberName))
            {
                materializeCall =
                    $"{Runtime}.UnwrapScalarProjectionAsync({materializeCall}, \"{memberName}\")";
            }

            return materializeCall;
        }

        return null;
    }

    private static string ApplyAsyncAwait(string rewritten, EfReplQueryRewriteOptions options)
    {
        if (!options.PreferAsyncQueries)
        {
            return rewritten;
        }

        var trimmed = rewritten.Trim();

        if (trimmed.StartsWith("await ", StringComparison.Ordinal)
            || !trimmed.Contains($"{Runtime}.", StringComparison.Ordinal)
            || !trimmed.Contains("Async(", StringComparison.Ordinal))
        {
            return rewritten;
        }

        return $"await {trimmed}";
    }

    private static string? TryRewriteQueryableSource(
        string source,
        Type? dbContextType,
        EfReplQueryRewriteOptions options = default)
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
                "Select" => ApplySelect(rewritten, argument, dbContextType, ref currentElementType, options),
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
        ref Type? currentElementType,
        EfReplQueryRewriteOptions options = default)
    {
        var inputElementType = currentElementType;
        var rewritten = FormatSelectCall(source, selector, dbContextType, inputElementType, options);

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

    internal static string? TryRewriteEmbeddedTerminalCalls(
        string snippet,
        Type dbContextType,
        EfReplQueryRewriteOptions options = default)
    {
        if (!LooksLikeAnonymousTypeCreation(snippet)
            || !snippet.Contains("db.", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            var tree = CSharpSyntaxTree.ParseText(
                snippet,
                CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
            var rewriter = new EmbeddedTerminalInvocationRewriter(dbContextType, options);
            var rewritten = rewriter.Visit(tree.GetRoot());

            return rewriter.Changed ? rewritten.ToFullString() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TryParseDbSetSource(string source, out string propertyName, out string middle)
    {
        propertyName = string.Empty;
        middle = string.Empty;

        if (!source.StartsWith("db.", StringComparison.Ordinal))
        {
            return false;
        }

        var afterDb = source["db.".Length..];
        var dotIndex = afterDb.IndexOf('.');

        if (dotIndex < 0)
        {
            propertyName = afterDb;
            return !string.IsNullOrWhiteSpace(propertyName);
        }

        propertyName = afterDb[..dotIndex];
        middle = afterDb[dotIndex..];
        return !string.IsNullOrWhiteSpace(propertyName);
    }

    private static bool LooksLikeAnonymousTypeCreation(string snippet)
    {
        return snippet.Contains("new", StringComparison.Ordinal)
               && snippet.Contains('{', StringComparison.Ordinal);
    }

    private static bool IsEmbeddedTerminalMethod(string methodName)
    {
        return methodName is "Count"
            or "CountAsync"
            or "First"
            or "FirstAsync"
            or "FirstOrDefault"
            or "FirstOrDefaultAsync"
            or "Single"
            or "SingleAsync"
            or "SingleOrDefault"
            or "SingleOrDefaultAsync"
            or "Any"
            or "AnyAsync";
    }

    private sealed class EmbeddedTerminalInvocationRewriter(
        Type dbContextType,
        EfReplQueryRewriteOptions options) : CSharpSyntaxRewriter
    {
        internal bool Changed { get; private set; }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;

            if (visited.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return visited;
            }

            var methodName = memberAccess.Name.Identifier.Text;

            if (!IsEmbeddedTerminalMethod(methodName))
            {
                return visited;
            }

            var source = memberAccess.Expression.ToString();

            if (!TryParseDbSetSource(source, out var propertyName, out var middle))
            {
                return visited;
            }

            string? predicate = null;

            if (visited.ArgumentList.Arguments.Count == 1)
            {
                predicate = visited.ArgumentList.Arguments[0].Expression.ToString();
            }
            else if (visited.ArgumentList.Arguments.Count > 1)
            {
                return visited;
            }

            var rewrite = TryBuildTerminalCall(dbContextType, propertyName, middle, methodName, predicate, options);

            if (rewrite is null)
            {
                return visited;
            }

            Changed = true;

            return SyntaxFactory.ParseExpression(rewrite).WithTriviaFrom(visited);
        }
    }
}