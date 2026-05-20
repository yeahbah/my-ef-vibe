namespace MyEfVibe.Tests;

public sealed class LinqEfQueryHeuristicsTests
{
    [Theory]
    [InlineData("return await DbContext.Products.ToListAsync();")]
    [InlineData("db.Employees.Where(e => e.Active)")]
    public void LooksLikeEfQuery_DbContextMarkers_ReturnsTrue(string statement)
    {
        Assert.True(LinqEfQueryHeuristics.LooksLikeEfQuery(statement));
    }

    [Theory]
    [InlineData("return items.Where(x => x.Amount > 0).ToList();")]
    [InlineData("Request.Headers.FirstOrDefault()")]
    public void LooksLikeEfQuery_InMemoryOrHttpMarkers_ReturnsFalse(string statement)
    {
        Assert.False(LinqEfQueryHeuristics.LooksLikeEfQuery(statement));
    }
}
