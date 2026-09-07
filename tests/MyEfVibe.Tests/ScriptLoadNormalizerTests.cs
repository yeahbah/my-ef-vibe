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
    public void Normalize_does_not_auto_take_tolist_in_script_load()
    {
        var code = """
            var loadedUsers = db.Users.ToList();
            """;

        var normalized = ScriptLoadNormalizer.Normalize(code, typeof(FakeRewriterDbContext), preserveAsyncQueries: false);

        Assert.DoesNotContain("Take(100)", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain(".Take(100)", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_does_not_stub_load_file_take_argument_to_zero()
    {
        var code = """
            int pageSize = 25;
            Task<List<FakeRewriterUser>> LoadPage() => db.Users.Take(pageSize).ToListAsync();
            """;

        var normalized = ScriptLoadNormalizer.Normalize(code, typeof(FakeRewriterDbContext), preserveAsyncQueries: false);

        Assert.Contains("Take(pageSize)", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("Take(0)", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain(".Take(0)", normalized, StringComparison.Ordinal);
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

    [Fact]
    public async Task InitializeAsync_script_load_tolist_keeps_all_rows()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "efvibe-script-normalize-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var helperPath = Path.Combine(tempDirectory, "load-all.csx");
            await File.WriteAllTextAsync(
                helperPath,
                """
                var loadedUsers = db.Users.ToList();
                """);

            var options = new DbContextOptionsBuilder<FakeRewriterDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var context = new FakeRewriterDbContext(options);
            for (var id = 1; id <= 150; id++)
            {
                context.Users.Add(new FakeRewriterUser { Id = id, Name = $"user-{id}" });
            }

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
                    LoadPaths = ["load-all.csx"],
                    SearchPaths = [tempDirectory],
                },
                scriptSearchBasePath: tempDirectory);

            await session.InitializeAsync(tempDirectory);

            var result = await session.EvaluateAsync("loadedUsers");
            var loadedCount = ((System.Collections.IEnumerable)result!).Cast<object>().Count();

            Assert.Equal(150, loadedCount);
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
