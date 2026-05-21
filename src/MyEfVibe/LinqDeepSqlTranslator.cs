using System.Reflection;

namespace MyEfVibe;

internal sealed record LinqSqlTranslationResult(
    string? Sql,
    string? Note,
    string? QueryPlan = null,
    string? QueryPlanNote = null);

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

        var entityTypeNames = DbSetEntityDiscovery.DiscoverEntityTypeNames(session.DbContext);
        var representativeEntity = DbSetEntityDiscovery.SelectRepresentativeEntityName(entityTypeNames);

        if (OpenGenericProbeBinder.ContainsOpenGenericTypeParameter(statementOrCode)
            && representativeEntity is null)
        {
            return new LinqSqlTranslationResult(
                null,
                "Open generic repository query (Set<T>) — no DbSet entity types found on the DbContext.");
        }

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(statementOrCode, representativeEntity);

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

            string? sql = null;

            if (sqlLiteral is string sqlFromScript && !string.IsNullOrWhiteSpace(sqlFromScript))
                sql = sqlFromScript;
            else
            {
                var queryable = await session.EvaluateProbeAsync(scriptProbe, cancellationToken);

                if (!RelationalQueryableSqlFormatter.TryGetSql(
                        queryable,
                        formatterAssemblies,
                        out sql))
                {
                    return new LinqSqlTranslationResult(
                        null,
                        "Provider does not support ToQueryString() for this expression.");
                }
            }

            return await AttachQueryPlanAsync(
                session,
                host,
                sql,
                cancellationToken);
        }
        catch (Exception failure)
        {
            return new LinqSqlTranslationResult(null, TruncateNote(failure.Message));
        }
    }

    private static async Task<LinqSqlTranslationResult> AttachQueryPlanAsync(
        ScriptSession session,
        WorkspaceHost host,
        string sql,
        CancellationToken cancellationToken)
    {
        // Discovery assemblies intentionally skip Microsoft.EntityFrameworkCore.* in bin/;
        // EXPLAIN needs RelationalDatabaseFacadeExtensions from the loaded EF assemblies.
        var plan = await QueryPlanRunner.TryExplainAsync(
            session.DbContext,
            sql,
            host.EnumerateLoadedAssemblies(),
            cancellationToken);

        return new LinqSqlTranslationResult(
            sql,
            null,
            plan.PlanText,
            plan.Note);
    }

    private static string TruncateNote(string message)
    {
        var singleLine = message.ReplaceLineEndings(" ").Trim();

        return singleLine.Length <= 160
            ? singleLine
            : singleLine[..157] + "...";
    }
}
