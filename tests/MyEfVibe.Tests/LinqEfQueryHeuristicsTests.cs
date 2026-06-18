using MyEfVibe.Linq;

namespace MyEfVibe.Tests;

public sealed class LinqEfQueryHeuristicsTests
{
    [Theory]
    [InlineData("return Ok(_context.Cities);")]
    [InlineData("var entity = _context.BuyingGroups.Where(x => x.BuyingGroupId == key);")]
    [InlineData("await _dbContext.Orders.ToListAsync();")]
    [InlineData("db.Products.Count();")]
    public void LooksLikeEfQuery_IncludesCommonDbContextFieldNames(string statement)
    {
        Assert.True(LinqEfQueryHeuristics.LooksLikeEfQuery(statement));
    }

    [Theory]
    [InlineData("context.Features.Get<IExceptionHandlerFeature>();")]
    [InlineData("Request.Headers.Accept")]
    [InlineData("filters.Values.ToList();")]
    public void LooksLikeEfQuery_ExcludesNonEfCode(string statement)
    {
        Assert.False(LinqEfQueryHeuristics.LooksLikeEfQuery(statement));
    }
}