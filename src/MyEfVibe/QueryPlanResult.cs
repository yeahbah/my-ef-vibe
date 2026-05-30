namespace MyEfVibe;

internal sealed record QueryPlanResult(string? PlanText, string? Note)
{
    internal static QueryPlanResult Succeeded(IReadOnlyList<string> rows)
    {
        return new QueryPlanResult(string.Join(Environment.NewLine, rows), null);
    }

    internal static QueryPlanResult Failed(string note)
    {
        return new QueryPlanResult(null, note);
    }
}