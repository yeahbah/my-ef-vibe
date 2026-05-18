namespace MyEfVibe;

internal sealed record LinqSqlTranslationResult(string? Sql, string? Note);

internal static class LinqDeepSqlTranslator
{
    internal static async Task<LinqSqlTranslationResult> TranslateAsync(
        ScriptSession session,
        WorkspaceHost host,
        string statementOrCode,
        CancellationToken cancellationToken = default)
    {
        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(statementOrCode);

        if (probe is null)
        {
            return new LinqSqlTranslationResult(
                null,
                "Could not derive an IQueryable probe (unsupported syntax or terminal operator).");
        }

        try
        {
            var queryable = await session.EvaluateAsync(probe, cancellationToken);

            if (RelationalQueryableSqlFormatter.TryGetSql(
                    queryable,
                    host.EnumerateLoadedAssemblies(),
                    out var sql))
            {
                return new LinqSqlTranslationResult(sql, null);
            }

            return new LinqSqlTranslationResult(
                null,
                "Provider does not support ToQueryString() for this expression.");
        }
        catch (Exception failure)
        {
            return new LinqSqlTranslationResult(null, TruncateNote(failure.Message));
        }
    }

    private static string TruncateNote(string message)
    {
        var singleLine = message.ReplaceLineEndings(" ").Trim();

        return singleLine.Length <= 160
            ? singleLine
            : singleLine[..157] + "...";
    }
}
