using CommandLine;

namespace MyEfVibe.Tests;

public sealed class ScriptCliOptionsTests
{
    [Fact]
    public void Serve_parses_semicolon_separated_script_options()
    {
        var result = Parser.Default.ParseArguments<ServeCliOptions>(
        [
            "-p", "App.csproj",
            "--script-search-path", "/tmp/scripts",
            "--script-load", "Helpers.csx;Filters.csx",
            "--script-using", "MyApp.Helpers;System.Globalization"
        ]);

        result.WithParsed(options =>
        {
            Assert.Equal(["/tmp/scripts"], options.ScriptSearchPath);
            Assert.Equal(["Helpers.csx", "Filters.csx"], options.ScriptLoad);
            Assert.Equal(["MyApp.Helpers", "System.Globalization"], options.ScriptUsing);
        });
    }

    [Fact]
    public void Main_parses_semicolon_separated_script_options()
    {
        var result = Parser.Default.ParseArguments<EfvibeCliOptions>(
        [
            "-p", "App.csproj",
            "--script-load", "Helpers.csx;Filters.csx",
            "--script-using", "MyApp.Helpers;System.Globalization"
        ]);

        result.WithParsed(options =>
        {
            Assert.Equal(["Helpers.csx", "Filters.csx"], options.ScriptLoad);
            Assert.Equal(["MyApp.Helpers", "System.Globalization"], options.ScriptUsing);
        });
    }
}
