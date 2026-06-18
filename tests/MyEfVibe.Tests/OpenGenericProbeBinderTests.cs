using MyEfVibe.Linq;

namespace MyEfVibe.Tests;

public sealed class OpenGenericProbeBinderTests
{
    [Fact]
    public void Bind_SetGeneric_ReplacesTWithConcreteEntity()
    {
        var bound = OpenGenericProbeBinder.Bind("db.Set<T>()", "Product");

        Assert.Equal("db.Set<Product>()", ProbeTestHelper.CollapseWhitespace(bound));
        ProbeTestHelper.AssertParsesAsScript(bound);
    }

    [Fact]
    public void TryCreateProbeExpression_GenericRepository_UsesRepresentativeEntity()
    {
        const string statement = "return await DbContext.Set<T>().ToListAsync(cancellationToken);";

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(statement, "Product");

        Assert.NotNull(probe);
        Assert.Contains("Set<Product>", probe, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(probe);
    }

    [Theory]
    [InlineData("T", true)]
    [InlineData("TEntity", true)]
    [InlineData("Product", false)]
    [InlineData("Table", false)]
    public void IsOpenTypeParameterName_ClassifiesNames(string name, bool expected)
    {
        Assert.Equal(expected, OpenGenericProbeBinder.IsOpenTypeParameterName(name));
    }
}