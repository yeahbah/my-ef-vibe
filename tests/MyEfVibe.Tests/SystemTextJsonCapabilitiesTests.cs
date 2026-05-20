namespace MyEfVibe.Tests;

public sealed class SystemTextJsonCapabilitiesTests
{
    [Fact]
    public void IsCompatibleLoaded_WhenFrameworkJsonIsLoaded_ReturnsTrue()
    {
        _ = typeof(System.Text.Json.JsonSerializerOptions).Assembly;

        Assert.True(SystemTextJsonCapabilities.IsCompatibleLoaded());
    }
}
