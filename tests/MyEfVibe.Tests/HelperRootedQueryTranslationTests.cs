using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.EntityFrameworkCore;
using MyEfVibe.Linq;

namespace MyEfVibe.Tests;

public sealed class HelperRootedQueryTranslationTests : IDisposable
{
    private readonly string _scriptDirectory;

    public HelperRootedQueryTranslationTests()
    {
        _scriptDirectory = Path.Combine(Path.GetTempPath(), "efvibe-helper-query-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scriptDirectory);

        File.WriteAllText(
            Path.Combine(_scriptDirectory, "constants.csx"),
            """
            const int DefaultTake = 25;
            const decimal MinListPrice = 0m;
            """);

        File.WriteAllText(
            Path.Combine(_scriptDirectory, "helpers.csx"),
            """
            public record ProductSummaryDto
            {
                public int ProductId { get; init; }
                public string? Name { get; init; }
                public decimal ListPrice { get; init; }
            }
            """);

        File.WriteAllText(
            Path.Combine(_scriptDirectory, "product-filters.csx"),
            """
            #load "constants.csx"

            using System.Linq.Expressions;

            Expression<Func<QueryCompProduct, bool>> IsActiveProduct =
                product => product.ListPrice > MinListPrice;

            IQueryable<QueryCompProduct> ActiveProducts() =>
                db.Products.Where(IsActiveProduct);
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_scriptDirectory))
        {
            Directory.Delete(_scriptDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ActiveProducts_helper_chain_probe_evaluates_to_translatable_queryable()
    {
        var session = await CreateSessionAsync();

        const string statement = """
                                 ActiveProducts()
                                   .OrderBy(product => product.Name)
                                   .Take(DefaultTake)
                                   .Select(product => new ProductSummaryDto
                                   {
                                       ProductId = product.ProductId,
                                       Name = product.Name,
                                       ListPrice = product.ListPrice,
                                   })
                                   .ToList();
                                 """;

        var probe = LinqDeepExpressionAdapter.TryCreateProbeExpression(
            statement,
            nameof(QueryCompProduct),
            typeof(QueryCompProductDbContext),
            nameof(QueryCompProduct),
            preserveIdentifiers: session.GetDefinedIdentifiers());

        Assert.NotNull(probe);
        Assert.StartsWith("ActiveProducts()", probe, StringComparison.Ordinal);
        Assert.Contains(".Take(DefaultTake)", probe, StringComparison.Ordinal);
        Assert.DoesNotContain(".Take(0)", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("0()", probe, StringComparison.Ordinal);
        ProbeTestHelper.AssertParsesAsScript(probe);

        var sql = await session.EvaluateProbeAsync(ProbeScriptFormatter.ToQueryStringProbe(probe!));

        Assert.IsType<string>(sql);
        var sqlText = (string)sql!;

        Assert.Contains("Product", sqlText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ListPrice", sqlText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT 0", sqlText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIMIT 0", sqlText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("25", sqlText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActiveProducts_repository_style_evaluation_preserves_DefaultTake()
    {
        var session = await CreateSessionAsync(seedProducts: true);

        Assert.Contains("DefaultTake", session.GetDefinedIdentifiers());

        const string snippet = """
                               await ActiveProducts()
                                   .OrderBy(product => product.Name)
                                   .Take(DefaultTake)
                                   .ToListAsync(cancellationToken);
                               """;

        var result = await session.EvaluateAsync(snippet);

        Assert.NotNull(result);
        var list = Assert.IsAssignableFrom<System.Collections.ICollection>(result);
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void LooksLikeEfQuery_accepts_active_products_helper_chain()
    {
        const string statement = """
                                 ActiveProducts()
                                   .OrderBy(product => product.Name)
                                   .Take(DefaultTake)
                                   .Select(product => new ProductSummaryDto
                                   {
                                       ProductId = product.ProductId,
                                       Name = product.Name,
                                       ListPrice = product.ListPrice,
                                   })
                                   .ToList();
                                 """;

        Assert.True(LinqEfQueryHeuristics.LooksLikeEfQuery(statement));
    }

    private async Task<ScriptSession> CreateSessionAsync(bool seedProducts = false)
    {
        var options = new DbContextOptionsBuilder<QueryCompProductDbContext>()
            .UseSqlite($"Data Source=helper-query-{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
            .Options;

        var context = new QueryCompProductDbContext(options);
        await context.Database.EnsureCreatedAsync();

        if (seedProducts)
        {
            context.Products.AddRange(
                new QueryCompProduct { ProductId = 1, Name = "A", ListPrice = 10m },
                new QueryCompProduct { ProductId = 2, Name = "B", ListPrice = 20m },
                new QueryCompProduct { ProductId = 3, Name = "C", ListPrice = 30m });
            await context.SaveChangesAsync();
        }

        using var assemblyLoader = new InteractiveAssemblyLoader();
        assemblyLoader.RegisterDependency(typeof(QueryCompProductDbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(DbContext).Assembly);
        assemblyLoader.RegisterDependency(typeof(ReplQueryableRuntime).Assembly);

        var workspaceAssemblyPaths = new[]
            {
                typeof(QueryCompProductDbContext).Assembly.Location,
                typeof(DbContext).Assembly.Location,
                typeof(ReplQueryableRuntime).Assembly.Location,
            }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        var session = new ScriptSession(
            typeof(QueryCompProductDbContext),
            context,
            workspaceAssemblyPaths,
            assemblyLoader,
            configuration: new ScriptSessionConfiguration
            {
                LoadPaths = ["constants.csx", "helpers.csx", "product-filters.csx"],
                SearchPaths = [_scriptDirectory]
            },
            scriptSearchBasePath: _scriptDirectory);

        await session.InitializeAsync(_scriptDirectory);

        return session;
    }
}
