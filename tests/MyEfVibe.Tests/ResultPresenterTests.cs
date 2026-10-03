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

    [Fact]
    public void Present_formats_byte_array_as_base64_not_one_row_per_byte()
    {
        var content = new byte[] { 0x00, 0xFF, 0x10 };
        using var writer = new StringWriter();

        ResultPresenter.Present(content, writer);

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal([Convert.ToBase64String(content)], lines);
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

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}