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
    [InlineData("DECLARE @p int = 25;\n\nSELECT TOP(@p) 1", true)]
    [InlineData("DECLARE @p int = 25; SELECT TOP(@p) 1", true)]
    [InlineData("SET NOCOUNT ON;\nSELECT 1", true)]
    [InlineData("SET @p = 25;\nSELECT @p", true)]
    [InlineData("INSERT INTO Products (Name) VALUES ('x'); SELECT * FROM Products", true)]
    public void LooksLikeQuery_classifies_statements(string sql, bool expected)
    {
        Assert.Equal(expected, RawSqlClassifier.LooksLikeQuery(sql));
    }

    [Fact]
    public void ContainsQueryStatement_finds_select_after_declare()
    {
        const string sql = "DECLARE @p int = 25; SELECT TOP(@p) 1";

        Assert.True(RawSqlClassifier.ContainsQueryStatement(sql));
    }

    [Theory]
    [InlineData("SELECT 1", "SELECT 1")]
    [InlineData("SELECT COUNT(*) FROM Products; INSERT INTO Products (Name) VALUES ('x')", "SELECT COUNT(*) FROM Products")]
    [InlineData("INSERT INTO Products (Name) VALUES ('x'); SELECT COUNT(*) FROM Products", "SELECT COUNT(*) FROM Products")]
    [InlineData("SET NOCOUNT ON; SELECT 1; DELETE FROM Products", "SELECT 1")]
    [InlineData("DELETE FROM Products WHERE Id = 1", "DELETE FROM Products WHERE Id = 1")]
    public void SelectExplainableStatement_keeps_a_single_statement(string sql, string expected)
    {
        Assert.Equal(expected, RawSqlClassifier.SelectExplainableStatement(sql));
    }
}
