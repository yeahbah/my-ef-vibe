namespace MyEfVibe.Tests;

public sealed class SetEntityTypeExtractorTests
{
    [Fact]
    public void TryExtractConcreteEntityTypeName_finds_Currency_from_Set_call()
    {
        const string code = """
            return await _dbContext.Set<Currency>()
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            """;

        Assert.True(SetEntityTypeExtractor.TryExtractConcreteEntityTypeName(code, out var entityType));
        Assert.Equal("Currency", entityType);
    }

    [Fact]
    public void TryExtractConcreteEntityTypeName_returns_false_for_open_generic_Set()
    {
        const string code = "return await DbContext.Set<T>().ToListAsync(cancellationToken);";

        Assert.False(SetEntityTypeExtractor.TryExtractConcreteEntityTypeName(code, out _));
    }

    [Fact]
    public void TryExtractConcreteEntityTypeName_finds_Product_from_dbSet_property_chain()
    {
        const string code = "db.Products.AsNoTracking().Take(10).ToList();";

        Assert.False(SetEntityTypeExtractor.TryExtractConcreteEntityTypeName(code, out _));
    }
}
