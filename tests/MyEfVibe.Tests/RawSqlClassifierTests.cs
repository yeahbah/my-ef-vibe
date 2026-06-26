namespace MyEfVibe.Tests;

public sealed class RawSqlClassifierTests
{
    [Theory]
    [InlineData("SELECT 1", true)]
    [InlineData("  with cte as (select 1) select * from cte", true)]
    [InlineData("EXPLAIN SELECT 1", true)]
    [InlineData("INSERT INTO Products VALUES (1)", false)]
    [InlineData("UPDATE Products SET Name = 'x'", false)]
    [InlineData("DELETE FROM Products", false)]
    [InlineData("-- @p='10'\nSELECT 1", true)]
    [InlineData("/* probe */ SELECT 1", true)]
    public void LooksLikeQuery_classifies_statements(string sql, bool expected)
    {
        Assert.Equal(expected, RawSqlClassifier.LooksLikeQuery(sql));
    }
}
