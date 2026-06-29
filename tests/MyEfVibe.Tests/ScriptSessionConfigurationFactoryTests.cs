namespace MyEfVibe.Tests;

public sealed class ScriptSessionConfigurationFactoryTests
{
    [Fact]
    public void FromCliOptions_adds_resolved_search_directory_to_search_paths_and_base()
    {
        const string searchDirectory = "/tmp/my-solution";

        var configuration = ScriptSessionConfigurationFactory.FromCliOptions(
            ["./scripts"],
            ["helpers.csx"],
            ["MyApp.Helpers"],
            searchDirectory);

        Assert.Contains("./scripts", configuration.SearchPaths);
        Assert.Contains(searchDirectory, configuration.SearchPaths);
        Assert.Equal(searchDirectory, configuration.BasePath);
        Assert.Equal(["helpers.csx"], configuration.LoadPaths);
        Assert.Equal(["MyApp.Helpers"], configuration.AdditionalUsings);
    }

    [Fact]
    public void FromCliOptions_uses_script_base_path_for_relative_search_paths()
    {
        const string searchDirectory = "/tmp/my-solution/src";
        const string scriptBasePath = "/tmp/my-workspace";

        var configuration = ScriptSessionConfigurationFactory.FromCliOptions(
            ["scripts"],
            ["helpers.csx"],
            null,
            searchDirectory,
            scriptBasePath);

        var resolved = configuration.ResolveSearchPaths(searchDirectory);

        Assert.Equal(scriptBasePath, configuration.BasePath);
        Assert.Contains(Path.GetFullPath("/tmp/my-workspace/scripts"), resolved);
        Assert.Contains(Path.GetFullPath(searchDirectory), resolved);
    }
}
