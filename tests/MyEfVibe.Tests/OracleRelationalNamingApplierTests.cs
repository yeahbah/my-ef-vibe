namespace MyEfVibe.Tests;

public sealed class OracleRelationalNamingApplierTests
{
    [Theory]
    [InlineData(typeof(Guid), "VARCHAR2", true)]
    [InlineData(typeof(Guid), "NVARCHAR2", true)]
    [InlineData(typeof(Guid), "CHAR", true)]
    [InlineData(typeof(Guid), "RAW", false)]
    [InlineData(typeof(string), "VARCHAR2", false)]
    [InlineData(typeof(Guid), null, false)]
    public void RequiresGuidStringConversion_matches_guid_string_pairs(
        Type clrType,
        string? dataType,
        bool expected)
    {
        Assert.Equal(expected, OracleRelationalNamingApplier.RequiresGuidStringConversion(clrType, dataType));
    }
}
