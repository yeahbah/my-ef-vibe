namespace MyEfVibe.Tests;

public sealed class PackOutputConfigurationManagerTests
{
    [Fact]
    public void Publish_output_does_not_bundle_host_configuration_manager()
    {
        var publishDirectory = FindLatestPublishDirectory();

        if (publishDirectory is null)
        {
            return;
        }

        Assert.False(File.Exists(Path.Combine(publishDirectory, "System.Configuration.ConfigurationManager.dll")));
    }

    private static string? FindLatestPublishDirectory()
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "MyEfVibe", "bin"));

        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory
            .EnumerateDirectories(root, "publish", SearchOption.AllDirectories)
            .Select(path => new DirectoryInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => info.FullName)
            .FirstOrDefault();
    }
}
