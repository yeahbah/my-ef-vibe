namespace MyEfVibe;

internal sealed record QueryPlanResult(string? PlanText, string? Note)
{
    internal static QueryPlanResult Succeeded(IReadOnlyList<string> rows) =>
        new(string.Join(Environment.NewLine, rows), null);

    internal static QueryPlanResult Failed(string note) => new(null, note);
}
