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
        IEnumerable<string>? contextInstanceIdentifiers = null,
        CancellationToken cancellationToken = default)
    {
        if (!LinqEfQueryHeuristics.LooksLikeEfQuery(statementOrCode))
        {
            return new LinqSqlTranslationResult(
                null,
                "Not an EF Core query expression.");
        }

        var includedModelEntities = DbContextModelEntityDiscovery.DiscoverIncludedEntityTypeNames(session.DbContext);

        if (QueryableEntityTypeResolver.TryExtractConcreteEntityTypeName(
                statementOrCode,
                session.DbContext.GetType(),
                out var queryEntityType)
            && includedModelEntities.Count > 0
            && !includedModelEntities.Contains(queryEntityType))
        {
            return new LinqSqlTranslationResult(
                null,
                $"{queryEntityType} is not included in the model for this DbContext"
                + $" (no mapped table in the current provider configuration; SQL/EXPLAIN skipped).");
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

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(
            statementOrCode,
            representativeEntity,
            session.DbContext.GetType(),
            queryEntityType,
            contextInstanceIdentifiers);

        if (probe is null)
        {
            return new LinqSqlTranslationResult(
                null,
                "Could not derive an IQueryable probe (unsupported syntax or terminal operator).");
        }

        host.EnsureEntityFrameworkRelationalLoaded();

        var inspectionAssemblies = host.EnumerateLoadedAssemblies()
            .Concat(host.EnumerateDiscoveryAssemblies())
            .Distinct()
            .ToArray();

        try
        {
            var scriptProbe = ProbeScriptFormatter.ToScriptExpression(probe);

            var queryable = await session.EvaluateProbeAsync(scriptProbe, cancellationToken);

            if (!RelationalQueryableSqlFormatter.TryGetSql(queryable, inspectionAssemblies, out var sql))
            {
                if (queryable is not null
                    && !typeof(IQueryable).IsAssignableFrom(queryable.GetType()))
                {
                    return new LinqSqlTranslationResult(
                        null,
                        "Probe evaluated to IEnumerable — LINQ operators did not bind to IQueryable.");
                }

                return new LinqSqlTranslationResult(
                    null,
                    "Provider does not support ToQueryString() for this expression.");
            }

            return await AttachQueryPlanAsync(
                session,
                host,
                sql,
                cancellationToken);
        }
        catch (Exception failure)
        {
            if (TryCreateUnmappedEntityNote(failure, includedModelEntities, out var unmappedNote))
            {
                return new LinqSqlTranslationResult(null, unmappedNote);
            }

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
            host.ActiveProviderDescriptor,
            cancellationToken);

        return new LinqSqlTranslationResult(
            sql,
            null,
            plan.PlanText,
            plan.Note);
    }

    private static bool TryCreateUnmappedEntityNote(
        Exception failure,
        IReadOnlySet<string> includedModelEntities,
        out string note)
    {
        note = string.Empty;

        if (includedModelEntities.Count == 0)
        {
            return false;
        }

        var message = failure.Message;

        if (!message.Contains("not included in the model", StringComparison.OrdinalIgnoreCase)
            && !message.Contains("Cannot create a DbSet for", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        const string prefix = "Cannot create a DbSet for '";

        var start = message.IndexOf(prefix, StringComparison.Ordinal);

        if (start < 0)
        {
            return false;
        }

        start += prefix.Length;
        var end = message.IndexOf('\'', start);

        if (end <= start)
        {
            return false;
        }

        var entityType = message[start..end];

        if (includedModelEntities.Contains(entityType))
        {
            return false;
        }

        note =
            $"{entityType} is not included in the model for this DbContext"
            + $" (no mapped table in the current provider configuration; SQL/EXPLAIN skipped).";

        return true;
    }

    private static string TruncateNote(string message)
    {
        var singleLine = message.ReplaceLineEndings(" ").Trim();

        return singleLine.Length <= 160
            ? singleLine
            : singleLine[..157] + "...";
    }
}