using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class ErDiagramExporterTests
{
    [Fact]
    public void Export_writes_full_diagram_to_default_path()
    {
        var sessionDirectory = Path.Combine(Path.GetTempPath(), "efvibe-test-" + Guid.NewGuid());
        Directory.CreateDirectory(sessionDirectory);

        try
        {
            var options = new DbContextOptionsBuilder<DiagramTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            using var dbContext = new DiagramTestDbContext(options);

            ErDiagramExporter.Export(dbContext, sessionDirectory);

            var targetPath = Path.Combine(sessionDirectory, "DiagramTestDbContext-er-diagram.mmd");
            Assert.True(File.Exists(targetPath));

            var content = File.ReadAllText(targetPath);
            Assert.Contains("erDiagram", content, StringComparison.Ordinal);
            Assert.Contains("DiagramParent", content, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(sessionDirectory))
            {
                Directory.Delete(sessionDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Export_writes_filtered_diagram_to_custom_path()
    {
        var sessionDirectory = Path.Combine(Path.GetTempPath(), "efvibe-test-" + Guid.NewGuid());
        Directory.CreateDirectory(sessionDirectory);

        try
        {
            var options = new DbContextOptionsBuilder<DiagramTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            using var dbContext = new DiagramTestDbContext(options);
            var targetPath = Path.Combine(sessionDirectory, "children.mmd");

            ErDiagramExporter.Export(dbContext, sessionDirectory, "Children", targetPath);

            Assert.True(File.Exists(targetPath));

            var content = File.ReadAllText(targetPath);
            Assert.Contains("DiagramChild", content, StringComparison.Ordinal);
            Assert.DoesNotContain("DiagramUnrelated", content, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(sessionDirectory))
            {
                Directory.Delete(sessionDirectory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("/tmp/diagram.mmd", true)]
    [InlineData("./diagram.mmd", true)]
    [InlineData("out/diagram.mmd", true)]
    [InlineData("diagram.mmd", true)]
    [InlineData("Children", false)]
    public void LooksLikeExportPath_detects_file_paths(string value, bool expected)
    {
        Assert.Equal(expected, ErDiagramExporter.LooksLikeExportPath(value));
    }

    [Fact]
    public void ResolveDiagramExportPath_includes_entity_in_default_name()
    {
        var path = SessionPaths.ResolveDiagramExportPath(
            "/tmp/session",
            pathOrNull: null,
            dbContextName: "AppDbContext",
            entityName: "Orders");

        Assert.Equal("/tmp/session/AppDbContext-Orders-er-diagram.mmd", path);
    }
}
