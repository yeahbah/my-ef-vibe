namespace MyEfVibe;

internal static class LinqQueryInvocationNames
{
    internal static readonly HashSet<string> ScanTargets = new(StringComparer.Ordinal)
    {
        "Where",
        "Select",
        "SelectMany",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "GroupBy",
        "Join",
        "Skip",
        "Take",
        "AsEnumerable",
        "ToList",
        "ToListAsync",
        "ToArray",
        "ToArrayAsync",
        "Include",
        "ThenInclude",
        "FromSql",
        "FromSqlRaw",
        "ExecuteSqlRaw",
        "Single",
        "SingleAsync",
        "SingleOrDefault",
        "SingleOrDefaultAsync",
        "First",
        "FirstAsync",
        "FirstOrDefault",
        "FirstOrDefaultAsync",
        "Count",
        "CountAsync",
        "Any",
        "AnyAsync",
        "All",
        "AllAsync",
    };
}
