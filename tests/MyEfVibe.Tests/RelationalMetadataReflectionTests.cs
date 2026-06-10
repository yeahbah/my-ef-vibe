namespace MyEfVibe.Tests;

public sealed class RelationalMetadataReflectionTests
{
    [Fact]
    public void Resolve_is_thread_safe_under_parallel_load()
    {
        var anchor = new object();
        var exceptions = new List<Exception>();
        var resolvedCount = 0;

        Parallel.For(
            0,
            64,
            _ =>
            {
                try
                {
                    if (RelationalMetadataReflection.Resolve(anchor) is not null)
                    {
                        Interlocked.Increment(ref resolvedCount);
                    }
                }
                catch (Exception exception)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(exception);
                    }
                }
            });

        Assert.Empty(exceptions);
    }
}
