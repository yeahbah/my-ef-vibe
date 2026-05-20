namespace MyEfVibe.Tests;

public sealed class ProbeScriptFormatterTests
{
    [Fact]
    public void ToScriptExpression_MultilineQuery_CollapsesToSingleLine()
    {
        const string probe = """
            db.Employees
                .Include(e => e.Department)
                .Where(e => e.BusinessEntityId == businessEntityId)
            """;

        var script = ProbeScriptFormatter.ToScriptExpression(probe);

        Assert.Equal(
            "db.Employees .Include(e => e.Department) .Where(e => e.BusinessEntityId == businessEntityId)",
            script);
        Assert.Contains("e => e.BusinessEntityId", script, StringComparison.Ordinal);
    }

    [Fact]
    public void FindVarDeclarationEqualsIndex_SkipsEqualsInsideAnonymousType()
    {
        const string expression =
            "var raw = await db.Items.Select(g => new { Rating = g.Key, Count = g.Count() }).ToList();";

        var equalsIndex = ProbeScriptFormatter.FindVarDeclarationEqualsIndex(expression);

        Assert.True(equalsIndex >= 0);
        Assert.StartsWith("var raw =", expression[..(equalsIndex + 1)], StringComparison.Ordinal);
    }

    [Fact]
    public void ToScriptExpression_VarAssignment_StripsPrefixWithoutBreakingLambdas()
    {
        const string expression =
            "var query = db.Employees.Where(e => e.JobTitle == jobTitle).AsQueryable();";

        var script = ProbeScriptFormatter.ToScriptExpression(expression);

        Assert.Equal(
            "db.Employees.Where(e => e.JobTitle == jobTitle).AsQueryable()",
            script);
        Assert.Contains("e => e.JobTitle", script, StringComparison.Ordinal);
    }
}
