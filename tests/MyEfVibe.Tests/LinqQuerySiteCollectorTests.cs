namespace MyEfVibe.Tests;

public sealed class LinqQuerySiteCollectorTests
{
    [Fact]
    public void Collect_finds_injected_context_field_in_api_controller()
    {
        using var temp = new TempDirectory();
            var apiDir = Path.Combine(temp.Path, "Api");
            var dbDir = Path.Combine(temp.Path, "Database");
            Directory.CreateDirectory(apiDir);
            Directory.CreateDirectory(dbDir);

            var dbProject = Path.Combine(dbDir, "App.Database.csproj");
            File.WriteAllText(
                dbProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.0" />
                </Project>
                """);

            File.WriteAllText(
                Path.Combine(dbDir, "FakeRewriterDbContext.cs"),
                """
                using Microsoft.EntityFrameworkCore;
                public class FakeRewriterDbContext : DbContext
                {
                    public FakeRewriterDbContext(DbContextOptions<FakeRewriterDbContext> options) : base(options) { }
                    public DbSet<FakeRewriterUser> Users => Set<FakeRewriterUser>();
                }
                public class FakeRewriterUser { public int Id { get; set; } }
                """);

            var apiProject = Path.Combine(apiDir, "App.Api.csproj");
            File.WriteAllText(
                apiProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Database/App.Database.csproj" />
                  </ItemGroup>
                  <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.0" />
                </Project>
                """);

            File.WriteAllText(
                Path.Combine(apiDir, "EntitiesController.cs"),
                """
                using Microsoft.EntityFrameworkCore;
                public sealed class EntitiesController
                {
                    private readonly FakeRewriterDbContext _context;
                    public EntitiesController(FakeRewriterDbContext context) => _context = context;
                    public void GetCity(int key)
                    {
                        var entity = _context.Users.Where(x => x.Id == key);
                        if (!entity.Any()) { }
                    }
                }
                """);

        var sites = LinqQuerySiteCollector.Collect(
            dbProject,
            apiProject,
            typeof(FakeRewriterDbContext));

        Assert.True(sites.Count >= 2, $"Expected Where/Any sites, got {sites.Count}");
    }

    [Fact]
    public void Collect_finds_query_sites_with_nonstandard_context_field_name()
    {
        using var temp = new TempDirectory();
        var dbDir = Path.Combine(temp.Path, "Database");
        var apiDir = Path.Combine(temp.Path, "Api");
        Directory.CreateDirectory(dbDir);
        Directory.CreateDirectory(apiDir);

        var dbProject = Path.Combine(dbDir, "App.Database.csproj");
        File.WriteAllText(
            dbProject,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.0" />
            </Project>
            """);

        File.WriteAllText(
            Path.Combine(dbDir, "AppDbContext.cs"),
            """
            using Microsoft.EntityFrameworkCore;
            public class AppDbContext : DbContext
            {
                public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
                public DbSet<Widget> Widgets => Set<Widget>();
            }
            public class Widget { public int Id { get; set; } }
            """);

        var apiProject = Path.Combine(apiDir, "App.Api.csproj");
        File.WriteAllText(
            apiProject,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../Database/App.Database.csproj" />
              </ItemGroup>
              <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.0" />
            </Project>
            """);

        File.WriteAllText(
            Path.Combine(apiDir, "WidgetService.cs"),
            """
            using Microsoft.EntityFrameworkCore;
            public sealed class WidgetService
            {
                private readonly AppDbContext _inventory;
                public WidgetService(AppDbContext inventory) => _inventory = inventory;
                public void Load() => _inventory.Widgets.Where(w => w.Id > 0).ToList();
            }
            """);

        var sites = LinqQuerySiteCollector.Collect(dbProject, apiProject, "AppDbContext");

        Assert.Contains(sites, site => site.Statement.Contains("_inventory.Widgets", StringComparison.Ordinal));
    }

    [Fact]
    public void Collect_finds_sites_in_wide_world_importers_entities_controller_when_present()
    {
        var repoRoot = FindRepoRoot();
        var wwiRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Projects/WideWorldImporters/src");

        if (!Directory.Exists(wwiRoot))
            return;

        var efProject = Path.Combine(wwiRoot, "WideWorldImporters.Server.Database/WideWorldImporters.Server.Database.csproj");
        var apiProject = Path.Combine(wwiRoot, "WideWorldImporters.Server.Api/WideWorldImporters.Server.Api.csproj");

        if (!File.Exists(efProject) || !File.Exists(apiProject))
            return;

        var sites = LinqQuerySiteCollector.Collect(
            efProject,
            apiProject,
            "WideWorldImportersContext");

        Assert.True(
            sites.Count > 50,
            $"Expected many _context query sites in EntitiesController, got {sites.Count}.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MyEfVibe.slnx")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class TempDirectory : IDisposable
    {
        internal TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "efvibe-tests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
