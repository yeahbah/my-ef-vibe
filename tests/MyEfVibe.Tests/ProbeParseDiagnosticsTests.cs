namespace MyEfVibe.Tests;

public sealed class ProbeParseDiagnosticsTests
{
    [Fact]
    public void AnonymousTypeInSelect_StubRewritesProductId()
    {
        const string probe =
            "db.ProductReviews .AsNoTracking() .Where(x => x.ProductId == productId) .GroupBy(x => x.Rating) .Select(g => new { Rating = g.Key, Count = g.Count() })";

        var stubbed = ProbeParameterStubber.Stub(probe);

        Assert.DoesNotContain("productId", stubbed, StringComparison.Ordinal);
        Assert.Contains("ProductId == 0", stubbed, StringComparison.Ordinal);
    }
}