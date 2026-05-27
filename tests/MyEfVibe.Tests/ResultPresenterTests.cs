using System.Collections;
using System.Linq.Expressions;

namespace MyEfVibe.Tests;

public sealed class ResultPresenterTests
{
    [Fact]
    public void Present_DoesNotEnumerateDeferredQueryable()
    {
        var queryable = new ThrowOnEnumerateQueryable<FakeRewriterUser>();
        using var writer = new StringWriter();

        ResultPresenter.Present(queryable, writer);

        var output = writer.ToString();
        Assert.Contains("IQueryable<", output, StringComparison.Ordinal);
        Assert.Contains("deferred", output, StringComparison.Ordinal);
        Assert.Equal(0, queryable.EnumerationCount);
    }

    private sealed class ThrowOnEnumerateQueryable<T> : IQueryable<T>
    {
        internal int EnumerationCount { get; private set; }

        public Type ElementType => typeof(T);

        public Expression Expression { get; } = Expression.Constant(Array.Empty<T>().AsQueryable());

        public IQueryProvider Provider => Array.Empty<T>().AsQueryable().Provider;

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            throw new InvalidOperationException("Deferred query should not be enumerated by presentation.");
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
