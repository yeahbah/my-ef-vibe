namespace MyEfVibe.Linq;

internal static class LinqScanRecommendations
{
    internal static string Get(string ruleId)
    {
        return ruleId switch
        {
            "cartesian" =>
                "Use AsSplitQuery() to fetch related rows in separate SQL statements instead of one wide join.\n"
                + "Reduce includes: project with Select to a DTO, or load rare navigations with Entry/Collection.Load.\n"
                + "If you need several collections, split into multiple targeted queries rather than one mega-graph.",

            "client-eval" =>
                "Keep filtering, ordering, and projection on IQueryable so EF translates them to SQL.\n"
                + "Move Where/Select/OrderBy/Take before AsEnumerable(); only switch to IEnumerable after the DB work is done.\n"
                + "Prefer ToListAsync() on the composed IQueryable instead of materializing early.",

            "unbounded-materialize" =>
                "Bound the result: add Take/TakeAsync or paging (Skip + Take) before ToList/ToArray.\n"
                + "For read-only lists, add AsNoTracking() to reduce change-tracker overhead.\n"
                + "Project with Select(...) to fetch only the columns you need instead of full entities.",

            "unordered-take" =>
                "Add OrderBy or OrderByDescending before Take so row order (and paging) is deterministic.\n"
                + "Order by an indexed, stable key (often the primary key) for efficient paging.\n"
                + "Avoid relying on database default ordering — it can change between executions.",

            "first-without-take" =>
                "Filter before First: use FirstOrDefaultAsync(x => x.Key == id) or .Where(...).Take(1).FirstOrDefaultAsync().\n"
                + "Ensure `using Microsoft.EntityFrameworkCore` for EF async operators (not System.Linq.Async).\n"
                + "Run with :dblog on or :plan to confirm executed SQL includes LIMIT/TOP/FETCH.",

            "raw-sql" =>
                "Review execution plans and indexes for predicates in the SQL.\n"
                + "Prefer LINQ where possible; reserve raw SQL for cases EF cannot express.\n"
                + "Keep using separate SQL parameters (not string concatenation) for dynamic values.",

            "raw-sql-unparameterized" =>
                "Never build SQL with string concatenation or interpolation into FromSqlRaw/ExecuteSqlRaw.\n"
                + "Use ExecuteSqlRaw(sql, parameters) / FromSqlRaw(sql, parameters) with placeholders ({0}, @p0), or ExecuteSqlInterpolated / FromSqlInterpolated.\n"
                + "Review execution plans and indexes after fixing parameterization.",

            "n-plus-one" =>
                "Load related data up front with Include/ThenInclude, or one query Where(id in batch).\n"
                + "Inside the loop, read from an in-memory dictionary built from that batch — not the database.\n"
                + "If multiple collection includes are needed, combine with AsSplitQuery() to limit join explosion.",

            "query-site" =>
                "Compare translated SQL to intent — check filters, joins, and row multiplication.\n"
                + "Paste the expression in the REPL to run, benchmark, or use :plan on the live query.",

            "unmapped-entity" =>
                "This entity is not configured in the DbContext model for the active provider (e.g. SQLite LT subset).\n"
                + "Ignore scan results for this site when testing a reduced model, or switch provider/context.\n"
                + "To support the entity, add mapping/configuration and ensure the table exists for that provider.",

            "invalid-navigation-include" =>
                "The Include/ThenInclude chain references a navigation that is not available for this provider's model.\n"
                + "On SQLite LT, StateProvince and similar navigations are often Ignored — use scalar columns or drop the Include.\n"
                + "Fix lambda parameters so ThenInclude targets the previous navigation type (e.g. sp => sp.CountryRegion).",

            _ =>
                "Run the query with database logging (:dblog on) or :plan to inspect the executed SQL and row counts."
        };
    }
}