namespace MyEfVibe.Tests;

public sealed class ScriptAttributeParserTests
{
    [Fact]
    public void Parse_splits_compare_blocks_and_shared_preamble()
    {
        const string source = """
            var take = DefaultTake;

            #[Compare]
            ActiveProducts()
              .Take(take)
              .ToList();

            #[Compare]
            ActiveProducts()
              .AsNoTracking()
              .Take(take)
              .ToList();
            """;

        var blocks = ScriptAttributeParser.Parse(source);

        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, block => Assert.Equal("Compare", block.Attribute, ignoreCase: true));
        Assert.Contains("var take = DefaultTake;", blocks[0].Code, StringComparison.Ordinal);
        Assert.Contains(".AsNoTracking()", blocks[1].Code, StringComparison.Ordinal);
        Assert.DoesNotContain(".AsNoTracking()", blocks[0].Code, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetCompareBlocks_ignores_other_attributes()
    {
        const string source = """
            #[Benchmark(5)]
            db.Products.Count();

            #[Compare]
            db.Products.Take(1).ToList();
            """;

        Assert.True(ScriptAttributeParser.TryGetCompareBlocks(source, out var blocks));
        Assert.Single(blocks);
        Assert.Contains("db.Products.Take(1)", blocks[0].Code, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_reads_compare_description_parameter()
    {
        const string source = """
            #[Compare("With tracking")]
            ActiveProducts().ToList();

            #[Compare("No tracking")]
            ActiveProducts().AsNoTracking().ToList();

            #[Compare]
            ActiveProducts().Take(1).ToList();
            """;

        var blocks = ScriptAttributeParser.Parse(source);

        Assert.Equal(3, blocks.Count);
        Assert.Equal("With tracking", blocks[0].Parameter);
        Assert.Equal("No tracking", blocks[1].Parameter);
        Assert.Null(blocks[2].Parameter);
    }

    [Fact]
    public void Parse_accepts_single_quoted_compare_description()
    {
        const string source = """
            #[Compare('No tracking')]
            ActiveProducts().ToList();
            """;

        var blocks = ScriptAttributeParser.Parse(source);

        Assert.Single(blocks);
        Assert.Equal("No tracking", blocks[0].Parameter);
    }

    [Fact]
    public void TryGetBenchmarkBlock_returns_first_benchmark_block()
    {
        const string source = """
            var take = DefaultTake;

            #[Benchmark(10)]
            ActiveProducts()
              .Take(take)
              .ToList();
            """;

        Assert.True(ScriptAttributeParser.TryGetBenchmarkBlock(source, out var block));
        Assert.Equal("Benchmark", block!.Attribute, ignoreCase: true);
        Assert.Equal("10", block.Parameter);
        Assert.Contains("var take = DefaultTake;", block.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void GetBenchmarkIterations_uses_default_when_parameter_missing()
    {
        var block = new ScriptAttributedBlock("Benchmark", "db.Products.Count();", 1);

        Assert.Equal(5, ScriptAttributeParser.GetBenchmarkIterations(block));
    }

    [Fact]
    public void GetBenchmarkIterations_parses_numeric_parameter()
    {
        var block = new ScriptAttributedBlock("Benchmark", "db.Products.Count();", 1, "12");

        Assert.Equal(12, ScriptAttributeParser.GetBenchmarkIterations(block));
    }

    [Fact]
    public void StripScriptAttributeLines_removes_benchmark_attribute()
    {
        const string source = """
            #[Benchmark(10)]
            ActiveProducts()
              .ToList();
            """;

        var stripped = ScriptAttributeParser.StripScriptAttributeLines(source);

        Assert.DoesNotContain("#[", stripped, StringComparison.Ordinal);
        Assert.Contains("ActiveProducts()", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainsScriptAttributeLines_detects_benchmark_attribute()
    {
        const string source = """
            #[Benchmark(10)]
            ActiveProducts().ToList();
            """;

        Assert.True(ScriptAttributeParser.ContainsScriptAttributeLines(source));
    }
}
