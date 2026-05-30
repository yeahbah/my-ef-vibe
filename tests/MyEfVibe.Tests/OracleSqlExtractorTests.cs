namespace MyEfVibe.Tests;

public sealed class OracleSqlExtractorTests
{
    [Fact]
    public void TryExtractExplainableSql_DbmsSqlBlock_InlinesBindAndReturnsSelect()
    {
        const string plSql = """
                             DECLARE
                             l_sql     varchar2(32767);
                             l_cur     pls_integer;
                             BEGIN
                             l_cur := dbms_sql.open_cursor;
                             l_sql := 'SELECT "a"."PRODUCTID" FROM "AW_PRODUCT" "a" FETCH FIRST :p_p ROWS ONLY';
                             dbms_sql.parse(l_cur, l_sql, dbms_sql.native);
                             dbms_sql.bind_variable(l_cur, ':p_p', 10);
                             l_execute:= dbms_sql.execute(l_cur);
                             END;
                             """;

        var sql = OracleSqlExtractor.TryExtractExplainableSql(plSql);

        Assert.NotNull(sql);
        Assert.Contains("SELECT", sql, StringComparison.Ordinal);
        Assert.Contains("FETCH FIRST 10 ROWS ONLY", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(":p_p", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void TryExtractExplainableSql_PlainSelect_ReturnsUnchanged()
    {
        const string select = "SELECT * FROM AW_PRODUCT FETCH FIRST 5 ROWS ONLY";

        var sql = OracleSqlExtractor.TryExtractExplainableSql(select);

        Assert.Equal(select, sql);
    }
}