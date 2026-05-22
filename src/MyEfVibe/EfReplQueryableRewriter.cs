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

    [GeneratedRegex(@"db\.(\w+)\.Take\((.+)\)$", RegexOptions.CultureInvariant)]
    private static partial Regex SimpleTakeRegex();

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

        return TryRewriteSimpleTake(trimmed);
    }

    internal static string? TryCastDbSetRoots(string snippet, Type dbContextType) =>
        TryRewriteSimpleTake(snippet);

    /// <summary>
    /// Rewrites <c>.Where(...).Take(n)</c> on a <c>db.Set</c> root to runtime <see cref="Queryable"/> calls.
    /// </summary>
    internal static string? TryRewriteWhereTakePipeline(string snippet)
    {
        if (!TryParseTrailingCall(snippet, "Take", out var takeArgument, out var beforeTake))
            return null;

        string? selectArgument = null;

        if (TryParseTrailingCall(beforeTake, "Select", out selectArgument, out var beforeSelect))
            beforeTake = beforeSelect;

        if (!TryParseTrailingCall(beforeTake, "Where", out var predicate, out var source))
            return null;

        if (!source.StartsWith("db.", StringComparison.Ordinal))
            return null;

        var rewritten = $"{Runtime}.Where({source}, {predicate})";

        if (!string.IsNullOrWhiteSpace(selectArgument))
            rewritten = $"{Runtime}.Select({rewritten}, {selectArgument})";

        return $"{Runtime}.Take({rewritten}, {takeArgument})";
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

        if (terminal is "FirstAsync" or "FirstOrDefaultAsync" or "SingleAsync" or "SingleOrDefaultAsync")
            return BuildAsyncRuntimeCall(terminal, source, predicate);

        var runtimeMethod = MapToRuntimeMethod(terminal);

        if (runtimeMethod is null)
            return null;

        if (string.IsNullOrWhiteSpace(predicate))
            return $"{Runtime}.{runtimeMethod}({source})";

        return $"{Runtime}.{runtimeMethod}({source}, {predicate.Trim()})";
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

    private static string BuildAsyncRuntimeCall(string terminal, string source, string? predicate)
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
            : $"{Runtime}.{asyncMethod}({source}, {predicate.Trim()})";
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
