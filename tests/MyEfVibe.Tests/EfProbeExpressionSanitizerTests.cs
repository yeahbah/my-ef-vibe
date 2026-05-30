namespace MyEfVibe.Tests;

public sealed class EfProbeExpressionSanitizerTests
{
    [Fact]
    public void RemoveTranslationNeutralOperators_collapses_whitespace_before_member_access()
    {
        const string expression = "db.Employees .Include(e => e.Department) .AsNoTracking()";

        var sanitized = EfProbeExpressionSanitizer.RemoveTranslationNeutralOperators(expression);

        Assert.Equal("db.Employees.Include(e => e.Department)", sanitized);
    }
}