namespace MyEfVibe.Tests;

public sealed class QueryPlanRunnerCapabilityTests
{
    [Fact]
    public async Task TryExplainAsync_skips_plan_for_unknown_provider_without_throwing()
    {
        var descriptor = EntityFrameworkProviderCatalog.CreateDescriptor(
            "FirebirdSql.EntityFrameworkCore.Firebird");

        var result = await QueryPlanRunner.TryExplainAsync(
            new object(),
            "SELECT 1",
            [],
            descriptor);

        Assert.Null(result.PlanText);
        Assert.NotNull(result.Note);
        Assert.Contains("FirebirdSql.EntityFrameworkCore.Firebird", result.Note, StringComparison.Ordinal);
        Assert.Contains("LINQ", result.Note, StringComparison.OrdinalIgnoreCase);
    }
}
