using System.Reflection;
using System.Text.RegularExpressions;

namespace MyEfVibe;

/// <summary>
/// Rewrites REPL <c>db.*</c> queries so LINQ operators bind to <see cref="System.Linq.IQueryable{T}"/>
/// (not <see cref="System.Linq.Enumerable"/>), so EF translates them to SQL.
/// </summary>
internal static partial class EfReplQueryableRewriter
{
    private const string Queryable = "System.Linq.Queryable";

    [GeneratedRegex(
        @"^db\.(\w+)(.*)\.(First|FirstOrDefault|FirstAsync|FirstOrDefaultAsync)\(\)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SimpleTerminalRegex();

    [GeneratedRegex(@"db\.(\w+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex DbSetAccessRegex();

    internal static string? TryRewriteToEfStaticCalls(string snippet, Type? dbContextType)
    {
        if (dbContextType is null)
            return null;

        var trimmed = snippet.Trim().TrimEnd(';').Trim();

        if (string.IsNullOrWhiteSpace(trimmed)
            || !LinqEfQueryHeuristics.LooksLikeEfQuery(trimmed)
            || trimmed.Contains("System.Linq.Queryable.", StringComparison.Ordinal))
            return null;

        if (!trimmed.Contains("IQueryable<", StringComparison.Ordinal))
        {
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
        }

        return TryCastDbSetRoots(trimmed, dbContextType);
    }

    internal static string? TryCastDbSetRoots(string snippet, Type dbContextType)
    {
        if (snippet.Contains("IQueryable<", StringComparison.Ordinal))
            return null;

        var changed = false;

        var result = DbSetAccessRegex().Replace(
            snippet,
            match =>
            {
                var propertyName = match.Groups[1].Value;

                if (!TryResolveDbSetEntityTypeName(dbContextType, propertyName, out var entityTypeName))
                    return match.Value;

                changed = true;

                return $"((System.Linq.IQueryable<{entityTypeName}>)db.{propertyName})";
            });

        return changed ? result : null;
    }

    /// <summary>
    /// Rewrites <c>.Where(...).Take(n)</c> on a cast DbSet root to <see cref="Queryable"/> static calls
    /// so the probe stays an EF-translatable <see cref="IQueryable{T}"/>.
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

        if (!source.Contains("IQueryable<", StringComparison.Ordinal))
            return null;

        var rewritten = $"{Queryable}.Where({source}, {predicate})";

        if (!string.IsNullOrWhiteSpace(selectArgument))
            rewritten = $"{Queryable}.Select({rewritten}, {selectArgument})";

        return $"{Queryable}.Take({rewritten}, {takeArgument})";
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
        if (!TryResolveDbSetEntityTypeName(dbContextType, propertyName, out var entityTypeName))
            return null;

        var source = string.IsNullOrEmpty(middle)
            ? $"db.{propertyName}"
            : $"db.{propertyName}{middle}";

        var castSource = $"((System.Linq.IQueryable<{entityTypeName}>){source})";

        if (terminal is "FirstAsync" or "FirstOrDefaultAsync" or "SingleAsync" or "SingleOrDefaultAsync")
            return BuildAsyncExtensionCall(castSource, terminal, predicate);

        var queryableMethod = MapToQueryableMethod(terminal);

        if (queryableMethod is null)
            return null;

        if (string.IsNullOrWhiteSpace(predicate))
            return $"{Queryable}.{queryableMethod}({castSource})";

        return $"{Queryable}.{queryableMethod}({castSource}, {predicate.Trim()})";
    }

    private static string? MapToQueryableMethod(string terminal) =>
        terminal switch
        {
            "First" => "FirstOrDefault",
            "FirstOrDefault" => "FirstOrDefault",
            "Single" => "SingleOrDefault",
            "SingleOrDefault" => "SingleOrDefault",
            _ => null,
        };

    private static string BuildAsyncExtensionCall(string castSource, string terminal, string? predicate)
    {
        var asyncMethod = terminal switch
        {
            "First" => "FirstOrDefaultAsync",
            "FirstAsync" => "FirstOrDefaultAsync",
            "FirstOrDefault" => "FirstOrDefaultAsync",
            "FirstOrDefaultAsync" => "FirstOrDefaultAsync",
            "Single" => "SingleOrDefaultAsync",
            "SingleAsync" => "SingleOrDefaultAsync",
            "SingleOrDefault" => "SingleOrDefaultAsync",
            "SingleOrDefaultAsync" => "SingleOrDefaultAsync",
            _ => terminal,
        };

        return string.IsNullOrWhiteSpace(predicate)
            ? $"{castSource}.{asyncMethod}()"
            : $"{castSource}.{asyncMethod}({predicate.Trim()})";
    }

    private static bool TryResolveDbSetEntityTypeName(
        Type dbContextType,
        string propertyName,
        out string entityTypeName)
    {
        entityTypeName = string.Empty;

        var property = dbContextType.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null || !property.PropertyType.IsGenericType)
            return false;

        var genericDefinition = property.PropertyType.GetGenericTypeDefinition();

        if (genericDefinition.FullName?
                .StartsWith("Microsoft.EntityFrameworkCore.DbSet`1", StringComparison.Ordinal) != true)
            return false;

        var entityType = property.PropertyType.GetGenericArguments()[0];

        entityTypeName = entityType.FullName ?? entityType.Name;

        return true;
    }
}
