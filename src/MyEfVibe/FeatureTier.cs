namespace MyEfVibe;

internal enum FeatureTier
{
    Construct = 0,
    Linq = 1,
    Sql = 2,
    QueryPlan = 3,
    Conventions = 4
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
            FeatureTier.Linq =>
                "LINQ REPL and SQL++ translation (async queries only; query plans not available)",
            _ =>
                "DbContext construction"
        };
    }
}
