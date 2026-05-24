namespace MyEfVibe;

/// <summary>
/// Prepares repository-style snippets (await, DbContext field, locals, async terminals) for REPL evaluation.
/// </summary>
internal static class RepositorySnippetAdapter
{
    internal static string PrepareForEvaluation(string snippet, Type dbContextType)
    {
        if (string.IsNullOrWhiteSpace(snippet))
            return snippet;

        var normalized = ProbeScriptFormatter.ToScriptExpression(snippet);
        normalized = DbContextAliasSyntaxRewriter.Rewrite(normalized);
        normalized = ProbeParameterStubber.Stub(normalized, new ProbeStubContext(dbContextType, null));
        normalized = AsyncQueryableSyncRewriter.Rewrite(normalized);
        normalized = EfReplQueryableRewriter.TryRewriteToEfStaticCalls(normalized, dbContextType)
            ?? normalized;
        normalized = EfReplQueryableRewriter.TryCastDbSetRoots(normalized, dbContextType)
            ?? normalized;

        return EfReplQueryableRewriter.TryRewriteWhereTakePipeline(normalized, dbContextType)
            ?? EfReplQueryableRewriter.TryRewriteBareWhere(normalized, dbContextType)
            ?? normalized;
    }
}
