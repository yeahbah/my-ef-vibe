using Microsoft.Data.Sqlite;

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

    [Fact]
    public void SanitizeSqlForExplain_QuotedParameterValue_StaysInsideOneStringLiteral()
    {
        const string captured =
            """
            SELECT u."Name"
            FROM "Users" AS u
            WHERE u."Name" = @p0
              -- parameters: @p0 = 'x'; DROP TABLE Users; --'
            """;

        var explainable = QueryPlanRunner.SanitizeSqlForExplain(captured, MyEfVibeProvider.Sqlite);

        Assert.Contains("= '''x''; DROP TABLE Users; --'''", explainable, StringComparison.Ordinal);
        Assert.DoesNotContain("@p0", explainable, StringComparison.Ordinal);
        Assert.DoesNotContain("= 'x'; DROP TABLE", explainable, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeSqlForExplain_QuotedParameterValue_DoesNotDropTableDuringExplain()
    {
        const string captured =
            """
            SELECT u."Name"
            FROM "Users" AS u
            WHERE u."Name" = @p0
              -- parameters: @p0 = 'x'; DROP TABLE Users; --'
            """;

        var explainable = QueryPlanRunner.SanitizeSqlForExplain(captured, MyEfVibeProvider.Sqlite);
        var path = Path.Combine(Path.GetTempPath(), $"efvibe-plan-{Guid.NewGuid():N}.db");

        try
        {
            using var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();

            using (var setup = connection.CreateCommand())
            {
                setup.CommandText = """CREATE TABLE "Users" ("Name" TEXT); INSERT INTO "Users" VALUES ('a');""";
                setup.ExecuteNonQuery();
            }

            using (var explain = connection.CreateCommand())
            {
                explain.CommandText = "EXPLAIN QUERY PLAN " + explainable;
                using var reader = explain.ExecuteReader();
                while (reader.Read())
                {
                }
            }

            using var check = connection.CreateCommand();
            check.CommandText = """SELECT COUNT(*) FROM "Users";""";
            Assert.Equal(1L, (long)check.ExecuteScalar()!);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}