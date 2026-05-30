namespace MyEfVibe.Tests;

public sealed class SharedFrameworkCatalogTests
{
    [Fact]
    public void ResolveInstalledFrameworkDirectory_RollForwardToLatestPatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "efvibe-fw-" + Guid.NewGuid().ToString("N"));
        var frameworkRoot = Path.Combine(root, "Microsoft.AspNetCore.App");

        try
        {
            Directory.CreateDirectory(Path.Combine(frameworkRoot, "9.0.16"));

            var requested = Path.Combine(frameworkRoot, "9.0.0");
            var resolved = SharedFrameworkCatalog.ResolveInstalledFrameworkDirectory(requested);

            Assert.Equal(Path.Combine(frameworkRoot, "9.0.16"), resolved);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}