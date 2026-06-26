using System.Text.Json;
using MyEfVibe;

namespace MyEfVibe.Tests;

public sealed class TabularExportBuilderTests
{
    [Fact]
    public void ToJson_preserves_anonymous_type_property_declaration_order()
    {
        var rows = new List<object?>
        {
            new { ProductId = 1, Name = "A" },
            new { ProductId = 2, Name = "B" },
        };

        var json = TabularExportBuilder.ToJson(rows);

        using var document = JsonDocument.Parse(json);
        var propertyNames = document.RootElement[0]
            .EnumerateObject()
            .Select(static property => property.Name)
            .ToArray();

        Assert.Equal(["ProductId", "Name"], propertyNames);
    }

    [Fact]
    public void ToCsv_preserves_anonymous_type_property_declaration_order()
    {
        var rows = new List<object?>
        {
            new { ProductId = 1, Name = "A" },
        };

        var csv = TabularExportBuilder.ToCsv(rows);
        var header = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];

        Assert.Equal("ProductId,Name", header);
    }
}
