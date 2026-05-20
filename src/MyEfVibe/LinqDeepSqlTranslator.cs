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
        if (!LinqEfQueryHeuristics.LooksLikeEfQuery(statementOrCode))
        {
            return new LinqSqlTranslationResult(
                null,
                "Not an EF Core query expression.");
        }

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(statementOrCode);

        if (probe is null)
        {
            return new LinqSqlTranslationResult(
                null,
                "Could not derive an IQueryable probe (unsupported syntax or terminal operator).");
        }

        var formatterAssemblies = host.EnumerateDiscoveryAssemblies().ToArray();

        try
        {
            var scriptProbe = ProbeScriptFormatter.ToScriptExpression(probe);

            var sqlLiteral = await session.EvaluateProbeAsync(
                $"{scriptProbe}.ToQueryString()",
                cancellationToken);

            if (sqlLiteral is string sqlFromScript && !string.IsNullOrWhiteSpace(sqlFromScript))
                return new LinqSqlTranslationResult(sqlFromScript, null);

            var queryable = await session.EvaluateProbeAsync(scriptProbe, cancellationToken);

            if (RelationalQueryableSqlFormatter.TryGetSql(
                    queryable,
                    formatterAssemblies,
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
