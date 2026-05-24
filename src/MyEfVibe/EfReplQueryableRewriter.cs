using System.Reflection;
using System.Text.RegularExpressions;

namespace MyEfVibe;

/// <summary>
/// Rewrites REPL <c>db.*</c> queries so LINQ operators bind to <see cref="Queryable"/> / EF extensions
/// (not <see cref="Enumerable"/>), so EF translates them to SQL.
/// </summary>
internal static partial class EfReplQueryableRewriter
{
    private const string Runtime = "global::MyEfVibe.ReplQueryableRuntime";

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
            return null;

        var trimmed = snippet.Trim().TrimEnd(';').Trim();

        if (string.IsNullOrWhiteSpace(trimmed)
            || !LinqEfQueryHeuristics.LooksLikeEfQuery(trimmed)
            || trimmed.Contains(Runtime, StringComparison.Ordinal))
            return null;

        var simple = SimpleTerminalRegex().Match(trimmed);

        if (simple.Success)
        {
            var propertyName = simple.Groups[1].Value;
            var middle = simple.Groups[2].Value;
            var terminal = simple.Groups[3].Value;

            var terminalRewrite = TryBuildTerminalCall(dbContextType, propertyName, middle, terminal, predicate: null);

            if (terminalRewrite is not null)
                return terminalRewrite;
        }

        var terminalWithArgs = TryRewriteTerminalCallWithArguments(trimmed, dbContextType);

        if (terminalWithArgs is not null)
            return terminalWithArgs;

        var takeMaterialize = TryRewriteTakeThenMaterialize(trimmed, dbContextType);

        if (takeMaterialize is not null)
            return takeMaterialize;

        return TryRewriteSimpleTake(trimmed);
    }

    internal static string? TryCastDbSetRoots(string snippet, Type dbContextType) =>
        TryRewriteTakeThenMaterialize(snippet, dbContextType)
        ?? TryRewriteSimpleTake(snippet);

    /// <summary>
    /// Rewrites <c>.Where(...).Take(n)</c> on a <c>db.Set</c> root to runtime <see cref="Queryable"/> calls.
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
            return null;

        string? selectArgument = null;

        if (TryParseTrailingCall(beforeTake, "Select", out selectArgument, out var beforeSelect))
            beforeTake = beforeSelect;

        if (!TryParseTrailingCall(beforeTake, "Where", out var predicate, out var source))
            return null;

        if (!source.StartsWith("db.", StringComparison.Ordinal))
            return null;

        var dbSetPropertyName = source["db.".Length..].Split('.')[0];
        TryResolveEntityTypeForDbSetProperty(dbContextType, dbSetPropertyName, out var entityType);

        var rewritten = FormatWhereCall(source, predicate, dbContextType);

        if (!string.IsNullOrWhiteSpace(selectArgument))
            rewritten = FormatSelectCall(rewritten, selectArgument, dbContextType, entityType);

        rewritten = $"{Runtime}.Take({rewritten}, {takeArgument})";

        return materializeMethod is null
            ? rewritten
            : $"{Runtime}.{materializeMethod}({rewritten})";
    }

    /// <summary>
    /// Rewrites deferred <c>db.Set.Where(...)</c> probes (no terminal) so Roslyn binds <see cref="Queryable.Where"/>
    /// instead of <see cref="Enumerable.Where"/>.
    /// </summary>
    internal static string? TryRewriteBareWhere(string snippet, Type? dbContextType = null)
    {
        var working = snippet.Trim().TrimEnd(';').Trim();

        if (string.IsNullOrWhiteSpace(working)
            || !LinqEfQueryHeuristics.LooksLikeEfQuery(working)
            || working.Contains(Runtime, StringComparison.Ordinal))
            return null;

        if (!TryParseTrailingCall(working, "Where", out var predicate, out var source))
            return null;

        if (!source.StartsWith("db.", StringComparison.Ordinal))
            return null;

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
            entityType = resolvedFromSource;

        if (entityType is not null
            && TryGetSimpleSelectorResultType(entityType, selector, out var resultType))
        {
            return $"{Runtime}.Select<{FormatTypeNameForScript(entityType)}, {FormatTypeNameForScript(resultType)}>({source}, {selector})";
        }

        if (source.StartsWith("db.", StringComparison.Ordinal))
            return $"{source}.Select({selector})";

        return $"{source}.Select({selector})";
    }

    private static bool TryGetSimpleSelectorResultType(Type entityType, string selector, out Type resultType)
    {
        resultType = null!;

        var arrowIndex = selector.IndexOf("=>", StringComparison.Ordinal);

        if (arrowIndex < 0)
            return false;

        var body = selector[(arrowIndex + 2)..].Trim().TrimEnd(')');

        if (!body.Contains('.', StringComparison.Ordinal))
            return false;

        var memberName = body[(body.LastIndexOf('.') + 1)..];

        var property = entityType.GetProperty(
            memberName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null)
            return false;

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
            return null;

        return SqlTranslationProbe.TryExtractPredicateArgument(arguments) is { } predicate
            ? predicate.Trim()
            : null;
    }

    private static bool TryResolveEntityTypeForDbSetProperty(
        Type? dbContextType,
        string propertyName,
        out Type entityType) =>
        TryResolveEntityTypeForDbRoot($"db.{propertyName}", dbContextType, out entityType);

    private static string FormatTypeNameForScript(Type type)
    {
        if (string.IsNullOrEmpty(type.Namespace))
            return type.Name;

        return $"global::{type.FullName?.Replace('+', '.')}";
    }

    private static bool TryResolveEntityTypeForDbRoot(string source, Type? dbContextType, out Type entityType)
    {
        entityType = null!;

        if (dbContextType is null || !source.StartsWith("db.", StringComparison.Ordinal))
            return false;

        var afterDb = source["db.".Length..];
        var dotIndex = afterDb.IndexOf('.');

        var propertyName = (dotIndex < 0 ? afterDb : afterDb[..dotIndex]).Trim();

        if (string.IsNullOrWhiteSpace(propertyName))
            return false;

        if (!TryResolveDbSetProperty(dbContextType, propertyName))
            return false;

        var property = dbContextType.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property?.PropertyType is not { IsGenericType: true } propertyType)
            return false;

        if (propertyType.GetGenericTypeDefinition().FullName?
                .StartsWith("Microsoft.EntityFrameworkCore.DbSet`1", StringComparison.Ordinal) != true)
            return false;

        entityType = propertyType.GetGenericArguments()[0];

        return true;
    }

    private static string? TryRewriteTakeThenMaterialize(string snippet, Type dbContextType)
    {
        var match = TakeThenMaterializeRegex().Match(snippet.Trim().TrimEnd(';').Trim());

        if (!match.Success)
            return null;

        var propertyName = match.Groups[1].Value;

        if (!TryResolveDbSetProperty(dbContextType, propertyName))
            return null;

        var takeArgument = match.Groups[2].Value.Trim();
        var materializeMethod = match.Groups[3].Value;

        var takeCall = $"{Runtime}.Take(db.{propertyName}, {takeArgument})";

        return $"{Runtime}.{materializeMethod}({takeCall})";
    }

    private static string? TryRewriteSimpleTake(string snippet)
    {
        var match = SimpleTakeRegex().Match(snippet.Trim().TrimEnd(';').Trim());

        if (!match.Success)
            return null;

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
            return false;

        var openParenIndex = callIndex + suffix.Length - 1;

        if (!SqlTranslationProbe.TryExtractParenthesizedContent(snippet, openParenIndex, out argument)
            || !SqlTranslationProbe.TryFindClosingParenthesis(snippet, openParenIndex, out var closeParenIndex)
            || !SqlTranslationProbe.IsEndOfExpression(snippet, closeParenIndex + 1))
            return false;

        source = snippet[..callIndex].TrimEnd();

        return !string.IsNullOrWhiteSpace(source);
    }

    private static string? TryRewriteTerminalCallWithArguments(string expression, Type dbContextType)
    {
        foreach (var terminal in new[] { "FirstOrDefaultAsync", "FirstAsync", "FirstOrDefault", "First", "SingleOrDefaultAsync", "SingleAsync", "SingleOrDefault", "Single" })
        {
            var needle = $".{terminal}(";
            var index = expression.LastIndexOf(needle, StringComparison.Ordinal);

            if (index < 0)
                continue;

            var openParenIndex = index + needle.Length - 1;

            if (!SqlTranslationProbe.TryExtractParenthesizedContent(expression, openParenIndex, out var arguments))
                continue;

            if (!SqlTranslationProbe.IsEndOfExpression(expression, openParenIndex + arguments.Length + 2))
                continue;

            var source = expression[..index].TrimEnd();

            if (!source.StartsWith("db.", StringComparison.Ordinal))
                continue;

            var propertyName = source["db.".Length..].Split('.')[0];

            if (string.IsNullOrWhiteSpace(propertyName))
                continue;

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
            return null;

        var source = string.IsNullOrEmpty(middle)
            ? $"db.{propertyName}"
            : $"db.{propertyName}{middle}";

        var terminalPredicate = ExtractTerminalPredicate(predicate);

        if (terminal is "FirstAsync" or "FirstOrDefaultAsync" or "SingleAsync" or "SingleOrDefaultAsync")
            return BuildAsyncRuntimeCall(terminal, source, terminalPredicate, dbContextType, propertyName);

        var runtimeMethod = MapToRuntimeMethod(terminal);

        if (runtimeMethod is null)
            return null;

        if (string.IsNullOrWhiteSpace(terminalPredicate))
            return $"{Runtime}.{runtimeMethod}({source})";

        return FormatPredicateTerminalCall(runtimeMethod, source, terminalPredicate, dbContextType, propertyName);
    }

    private static string? MapToRuntimeMethod(string terminal) =>
        terminal switch
        {
            "First" => nameof(ReplQueryableRuntime.First),
            "FirstOrDefault" => nameof(ReplQueryableRuntime.FirstOrDefault),
            "Single" => nameof(ReplQueryableRuntime.Single),
            "SingleOrDefault" => nameof(ReplQueryableRuntime.SingleOrDefault),
            _ => null,
        };

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
            _ => null,
        };

        if (asyncMethod is null)
            return string.Empty;

        return string.IsNullOrWhiteSpace(predicate)
            ? $"{Runtime}.{asyncMethod}({source})"
            : FormatPredicateTerminalCall(asyncMethod, source, predicate, dbContextType, dbSetPropertyName);
    }

    private static bool TryResolveDbSetProperty(Type dbContextType, string propertyName)
    {
        var property = dbContextType.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null || !property.PropertyType.IsGenericType)
            return false;

        return property.PropertyType.GetGenericTypeDefinition().FullName?
            .StartsWith("Microsoft.EntityFrameworkCore.DbSet`1", StringComparison.Ordinal) == true;
    }
}
