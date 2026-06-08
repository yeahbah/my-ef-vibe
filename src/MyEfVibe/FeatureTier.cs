namespace MyEfVibe;

internal enum FeatureTier
{
    Construct = 0,
    Sql = 1,
    QueryPlan = 2,
    Conventions = 3
}

internal static class FeatureTierExtensions
{
    internal static string Describe(this FeatureTier tier)
    {
        return tier switch
        {
            FeatureTier.Conventions =>
                "Naming conventions, query plans (:plan), SQL translation, and LINQ REPL",
            FeatureTier.QueryPlan =>
                "Query plans (:plan), SQL translation, and LINQ REPL",
            FeatureTier.Sql =>
                "SQL translation and LINQ REPL (query plans not available for this provider yet)",
            _ =>
                "DbContext construction"
        };
    }
}
