using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class ScriptLoadNormalizerTests
{
    [Fact]
    public void Normalize_rewrites_where_helper_for_net8_style_script_load()
    {
        var code = """
            IQueryable<FakeRewriterUser> ActiveUsers() =>
                db.Users.Where(u => u.Name != "");
            """;

        var normalized = ScriptLoadNormalizer.Normalize(code, typeof(FakeRewriterDbContext), preserveAsyncQueries: false);

        Assert.Contains("ReplQueryableRuntime.Where", normalized, StringComparison.Ordinal);
        Assert.Contains("(global::System.Linq.IQueryable<", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("db.Users.Where(", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_loads_where_helper_after_normalization()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "efvibe-script-normalize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var helperPath = Path.Combine(tempDirectory, "user-filters.csx");
            await File.WriteAllTextAsync(
                helperPath,
                """
                IQueryable<FakeRewriterUser> ActiveUsers() =>
                    db.Users.Where(u => u.Name != "");
                """);

            var options = new DbContextOptionsBuilder<FakeRewriterDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var context = new FakeRewriterDbContext(options);
            context.Users.AddRange(
                new FakeRewriterUser { Id = 1, Name = "ada" },
                new FakeRewriterUser { Id = 2, Name = "bob" });
            await context.SaveChangesAsync();

            var assemblyLoader = new InteractiveAssemblyLoader();
            assemblyLoader.RegisterDependency(typeof(FakeRewriterDbContext).Assembly);
            assemblyLoader.RegisterDependency(typeof(DbContext).Assembly);

            var workspaceAssemblyPaths = new[]
                {
                    typeof(FakeRewriterDbContext).Assembly.Location,
                    typeof(DbContext).Assembly.Location,
                }
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

            var session = new ScriptSession(
                typeof(FakeRewriterDbContext),
                context,
                workspaceAssemblyPaths,
                assemblyLoader,
                configuration: new ScriptSessionConfiguration
                {
                    LoadPaths = ["user-filters.csx"],
                    SearchPaths = [tempDirectory],
                },
                scriptSearchBasePath: tempDirectory);

            await session.InitializeAsync(tempDirectory);

            var result = await session.EvaluateAsync("ActiveUsers().Count()");

            Assert.Equal(2, result);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }
}
