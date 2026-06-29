namespace MyEfVibe;

using System.Reflection;
using MyEfVibe.Linq;

/// <summary>
///     Rewrites embedded <c>db.*</c> LINQ in script load files so Roslyn binds query operators
///     against EF queryables (via <see cref="ReplQueryableRuntime" />) instead of in-memory
///     <see cref="Enumerable" /> when workspace and scripting metadata disagree (common on net8.0 + EF 9).
/// </summary>
internal static class ScriptLoadNormalizer
{
    internal static string Normalize(string code, Type? dbContextType, bool preserveAsyncQueries)
    {
        if (dbContextType is null || string.IsNullOrWhiteSpace(code))
        {
            return code;
        }

        var collapsed = CollapseExpressionContinuations(code);
        var lines = InputLineUtilities.SplitLines(collapsed);
        var rewritten = new string[lines.Length];
        var changed = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var next = TryRewriteDbQueriesInLine(line, dbContextType, preserveAsyncQueries);

            if (!string.Equals(next, line, StringComparison.Ordinal))
            {
                changed = true;
            }

            rewritten[index] = next;
        }

        return changed ? InputLineUtilities.JoinLines(rewritten) : code;
    }

    private static string CollapseExpressionContinuations(string code)
    {
        var lines = InputLineUtilities.SplitLines(code);
        var builder = new System.Text.StringBuilder();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            builder.Append(line);

            while (EndsWithArrow(line) && index + 1 < lines.Length)
            {
                index++;
                line = lines[index].TrimStart();
                builder.Append(' ');
                builder.Append(line);
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static bool EndsWithArrow(string line)
    {
        return line.TrimEnd().EndsWith("=>", StringComparison.Ordinal);
    }

    private static string TryRewriteDbQueriesInLine(
        string line,
        Type dbContextType,
        bool preserveAsyncQueries)
    {
        var dbIndex = line.IndexOf("db.", StringComparison.Ordinal);

        if (dbIndex < 0 || !LinqEfQueryHeuristics.LooksLikeEfQuery(line))
        {
            return line;
        }

        if (!TryExtractDbQueryExpression(line, dbIndex, out var expression, out var expressionStart, out var expressionEnd))
        {
            return line;
        }

        var rewritten = SnippetNormalizer.ForEvaluation(expression, dbContextType, preserveAsyncQueries);

        if (string.Equals(rewritten, expression, StringComparison.Ordinal))
        {
            return line;
        }

        var needsIQueryableCast = NeedsIQueryableCast(line, expressionStart, rewritten);

        if (needsIQueryableCast)
        {
            rewritten = WrapQueryableCast(rewritten, dbContextType, expression);
        }

        return string.Concat(line.AsSpan(0, expressionStart), rewritten, line.AsSpan(expressionEnd));
    }

    private static bool NeedsIQueryableCast(string line, int expressionStart, string rewritten)
    {
        if (!rewritten.Contains("ReplQueryableRuntime", StringComparison.Ordinal))
        {
            return false;
        }

        var prefix = line[..expressionStart];

        return prefix.Contains("IQueryable<", StringComparison.Ordinal)
               || prefix.Contains("=>", StringComparison.Ordinal);
    }

    private static string WrapQueryableCast(string rewritten, Type dbContextType, string originalExpression)
    {
        if (!TryResolveEntityTypeFromDbRoot(originalExpression, dbContextType, out var entityType))
        {
            return rewritten;
        }

        return $"(global::System.Linq.IQueryable<{FormatTypeName(entityType)}>)({rewritten})";
    }

    private static bool TryResolveEntityTypeFromDbRoot(string expression, Type dbContextType, out Type entityType)
    {
        entityType = null!;

        var trimmed = expression.Trim();

        if (!trimmed.StartsWith("db.", StringComparison.Ordinal))
        {
            return false;
        }

        var dotIndex = trimmed.IndexOf('.', 3);

        if (dotIndex < 0)
        {
            return false;
        }

        var propertyName = trimmed[3..dotIndex];
        var property = dbContextType.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null || !property.PropertyType.IsGenericType)
        {
            return false;
        }

        if (property.PropertyType.GetGenericTypeDefinition().FullName?
                .StartsWith("Microsoft.EntityFrameworkCore.DbSet`1", StringComparison.Ordinal)
            != true)
        {
            return false;
        }

        entityType = property.PropertyType.GetGenericArguments()[0];
        return true;
    }

    private static string FormatTypeName(Type type)
    {
        return type.FullName ?? type.Name;
    }

    private static bool TryExtractDbQueryExpression(
        string line,
        int dbIndex,
        out string expression,
        out int expressionStart,
        out int expressionEnd)
    {
        expression = string.Empty;
        expressionStart = dbIndex;
        expressionEnd = line.Length;

        var end = line.Length;

        while (end > dbIndex && char.IsWhiteSpace(line[end - 1]))
        {
            end--;
        }

        if (end > dbIndex && line[end - 1] == ';')
        {
            end--;
        }

        expressionEnd = end;
        expression = line[expressionStart..expressionEnd].TrimEnd();

        return expression.Length > 0;
    }
}
