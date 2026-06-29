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

    [Fact]
    public void ToJson_skips_navigation_properties_and_avoids_object_cycles()
    {
        var product = new CyclicProduct
        {
            ProductId = 1,
            Name = "Frame",
            ProductInventory = new CyclicProductInventory
            {
                ProductId = 1,
                Quantity = 5,
                Product = null!,
            },
        };
        product.ProductInventory.Product = product;

        var json = TabularExportBuilder.ToJson([product]);

        using var document = JsonDocument.Parse(json);
        var row = document.RootElement[0];
        var propertyNames = row.EnumerateObject().Select(static property => property.Name).ToArray();

        Assert.Equal(["ProductId", "Name"], propertyNames);
        Assert.Equal("Frame", row.GetProperty("Name").GetString());
    }

    [Fact]
    public void FormatScalar_formats_scalar_collections_without_json_cycles()
    {
        var formatted = TabularExportBuilder.FormatScalar(new[] { 1, 2, 3 });

        Assert.Equal("[1, 2, 3]", formatted);
    }

    private sealed class CyclicProduct
    {
        public int ProductId { get; set; }

        public string Name { get; set; } = string.Empty;

        public CyclicProductInventory ProductInventory { get; set; } = null!;
    }

    private sealed class CyclicProductInventory
    {
        public int ProductId { get; set; }

        public short Quantity { get; set; }

        public CyclicProduct Product { get; set; } = null!;
    }
}
