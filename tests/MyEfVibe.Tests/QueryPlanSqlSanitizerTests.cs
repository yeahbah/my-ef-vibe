namespace MyEfVibe.Tests;

public sealed class QueryPlanSqlSanitizerTests
{
    [Fact]
    public void SelectPlanSql_ReturnsRawCapturedEntryWithParameterComments()
    {
        const string captured =
            """
            [information] Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted
            SELECT p.productid
            FROM production.product AS p
            LIMIT @p
              -- parameters: @p = 10
              -- duration: 1 ms
            """;

        var selected = DbLogSqlExtractor.SelectPlanSql([captured], null);

        Assert.Equal(captured, selected);
    }

    [Fact]
    public void ExtractExecutableSql_VerboseCapture_StripsLogLinesAndKeepsSql()
    {
        const string captured =
            """
            [information] Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted
            SELECT p.productid
            FROM production.product AS p
            LIMIT @p
              -- parameters: @p = 10
              -- duration: 1 ms
            """;

        var executable = DbLogSqlExtractor.ExtractExecutableSql(captured);

        Assert.NotNull(executable);
        Assert.Contains("LIMIT @p", executable, StringComparison.Ordinal);
        Assert.DoesNotContain("-- parameters:", executable, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeSqlForExplain_VerboseNpgsqlCapture_InlinesLimitParameter()
    {
        const string captured =
            """
            [information] Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted
            SELECT p.productid
            FROM production.product AS p
            LIMIT @p
              -- parameters: @p = 10
              -- duration: 1 ms
            """;

        var explainable = QueryPlanRunner.SanitizeSqlForExplain(captured, MyEfVibeProvider.Npgsql);

        Assert.Contains("LIMIT 10", explainable, StringComparison.Ordinal);
        Assert.DoesNotContain("@p", explainable, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeSqlForExplain_ToQueryStringParameterWithDbTypeSuffix_InlinesParameter()
    {
        const string translated =
            """
            -- @__p_0='10' (DbType = Int32)
            SELECT p.productid
            FROM production.product AS p
            LIMIT @__p_0
            """;

        var explainable = QueryPlanRunner.SanitizeSqlForExplain(translated, MyEfVibeProvider.Npgsql);

        Assert.Contains("LIMIT 10", explainable, StringComparison.Ordinal);
        Assert.DoesNotContain("@__p_0", explainable, StringComparison.Ordinal);
        Assert.DoesNotContain("DbType", explainable, StringComparison.Ordinal);
    }
}