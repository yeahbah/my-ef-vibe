namespace MyEfVibe.Tests;

public sealed class RuntimeFrameworkConfigParserTests
{
    [Fact]
    public void ReadFrameworks_parses_runtimeconfig_without_system_text_json()
    {
        var path = Path.Combine(Path.GetTempPath(), $"efvibe-runtimeconfig-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "runtimeOptions": {
                    "tfm": "net8.0",
                    "frameworks": [
                      {
                        "name": "Microsoft.NETCore.App",
                        "version": "8.0.0"
                      },
                      {
                        "name": "Microsoft.AspNetCore.App",
                        "version": "8.0.11"
                      }
                    ]
                  }
                }
                """);

            var frameworks = RuntimeFrameworkConfigParser.ReadFrameworks(path).ToArray();

            Assert.Equal(2, frameworks.Length);
            Assert.Equal("Microsoft.NETCore.App", frameworks[0].Name);
            Assert.Equal("8.0.0", frameworks[0].Version);
            Assert.Equal("Microsoft.AspNetCore.App", frameworks[1].Name);
            Assert.Equal("8.0.11", frameworks[1].Version);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
