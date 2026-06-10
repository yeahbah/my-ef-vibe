namespace MyEfVibe;

internal readonly record struct EfReplQueryRewriteOptions(bool PreferAsyncQueries = false)
{
    internal static EfReplQueryRewriteOptions Sync => default;

    internal static EfReplQueryRewriteOptions Async => new(true);
}
