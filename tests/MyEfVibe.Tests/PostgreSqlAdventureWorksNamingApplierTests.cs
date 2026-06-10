namespace MyEfVibe.Tests;

public sealed class PostgreSqlAdventureWorksNamingApplierTests
{
    [Theory]
    [InlineData("ProductId", "ProductID")]
    [InlineData("ProductSubcategoryId", "ProductSubcategoryID")]
    [InlineData("BusinessEntityId", "BusinessEntityID")]
    public void ResolveColumnName_maps_id_suffix_to_uppercase_id(string propertyName, string expected)
    {
        var columns = new HashSet<string>(StringComparer.Ordinal)
        {
            expected,
            "Name"
        };

        var resolved = PostgreSqlAdventureWorksNamingApplier.ResolveColumnName(propertyName, columns);

        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData("ActivityLogId")]
    [InlineData("EntityId")]
    [InlineData("PersonTypeId")]
    public void ResolveColumnName_keeps_camel_case_id_when_database_uses_it(string propertyName)
    {
        var columns = new HashSet<string>(StringComparer.Ordinal)
        {
            propertyName
        };

        var resolved = PostgreSqlAdventureWorksNamingApplier.ResolveColumnName(propertyName, columns);

        Assert.Equal(propertyName, resolved);
    }

    [Fact]
    public void ResolveColumnName_maps_rowguid_to_lowercase_when_present()
    {
        var columns = new HashSet<string>(StringComparer.Ordinal)
        {
            "rowguid"
        };

        var resolved = PostgreSqlAdventureWorksNamingApplier.ResolveColumnName("Rowguid", columns);

        Assert.Equal("rowguid", resolved);
    }

    [Fact]
    public void ResolveColumnName_maps_rowguid_to_pascal_case_when_present()
    {
        var columns = new HashSet<string>(StringComparer.Ordinal)
        {
            "Rowguid"
        };

        var resolved = PostgreSqlAdventureWorksNamingApplier.ResolveColumnName("Rowguid", columns);

        Assert.Equal("Rowguid", resolved);
    }

    [Theory]
    [InlineData(typeof(bool), "smallint", true)]
    [InlineData(typeof(bool), "boolean", false)]
    [InlineData(typeof(int), "smallint", false)]
    [InlineData(typeof(bool), null, false)]
    public void RequiresBoolSmallintConversion_matches_bool_smallint_pairs(
        Type clrType,
        string? dataType,
        bool expected)
    {
        Assert.Equal(expected, PostgreSqlAdventureWorksNamingApplier.RequiresBoolSmallintConversion(clrType, dataType));
    }
}
