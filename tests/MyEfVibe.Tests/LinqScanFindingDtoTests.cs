namespace MyEfVibe.Tests;

public sealed class LinqScanFindingDtoTests
{
    [Fact]
    public void RoundTrip_PreservesSeverity()
    {
        var original = LinqScanFinding.Create("repo/Foo.cs", 10, "db.Items.Take(1)", "cartesian", "many includes");

        var dto = LinqScanFindingDto.From(original);
        var restored = dto.ToFinding();

        Assert.Equal(LinqScanSeverity.Warning, restored.Severity); // cartesian rule
        Assert.Equal(original.RuleId, restored.RuleId);
    }

    [Fact]
    public void ToFinding_MissingSeverityInJson_UsesRuleCatalog()
    {
        var dto = new LinqScanFindingDto(
            "repo/Foo.cs",
            5,
            "code",
            "n-plus-one",
            "loop query",
            null);

        var finding = dto.ToFinding();

        Assert.Equal(LinqScanSeverity.Error, finding.Severity);
    }
}