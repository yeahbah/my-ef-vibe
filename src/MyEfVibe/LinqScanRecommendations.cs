namespace MyEfVibe;

internal static class LinqScanRecommendations
{
    internal static string Get(string ruleId) =>
        ruleId switch
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

            _ =>
                "Run the query with database logging (:dblog on) or :plan to inspect the executed SQL and row counts.",
        };
}
