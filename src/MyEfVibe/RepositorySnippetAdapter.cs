namespace MyEfVibe;

/// <summary>
///     Prepares repository-style snippets (await, DbContext field, locals, async terminals) for REPL evaluation.
/// </summary>
internal static class RepositorySnippetAdapter
{
    internal static string PrepareForEvaluation(
        string snippet,
        Type dbContextType,
        bool preserveAsyncQueries = false)
    {
        if (string.IsNullOrWhiteSpace(snippet))
        {
            return snippet;
        }

        var normalized = ProbeScriptFormatter.ToScriptExpression(snippet, preserveAsyncQueries);
        normalized = DbContextAliasSyntaxRewriter.Rewrite(normalized);
        normalized = ProbeParameterStubber.Stub(
            normalized,
            new ProbeStubContext(dbContextType, null),
            preserveAsyncQueries);

        if (!preserveAsyncQueries)
        {
            normalized = AsyncQueryableSyncRewriter.Rewrite(normalized);
        }

        var options = preserveAsyncQueries
            ? EfReplQueryRewriteOptions.Async
            : EfReplQueryRewriteOptions.Sync;

        normalized = EfReplQueryableRewriter.TryRewriteToEfStaticCalls(normalized, dbContextType, options)
                     ?? normalized;
        normalized = EfReplQueryableRewriter.TryCastDbSetRoots(normalized, dbContextType, options)
                     ?? normalized;

        return EfReplQueryableRewriter.TryRewriteWhereTakePipeline(normalized, dbContextType, options)
               ?? EfReplQueryableRewriter.TryRewriteBareWhere(normalized, dbContextType)
               ?? normalized;
    }
}