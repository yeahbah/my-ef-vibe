namespace MyEfVibe.Tests;

public sealed class SystemTextJsonCapabilitiesTests
{
    [Fact]
    public void WebPropertySupported_WhenFrameworkJsonIsLoaded_ReturnsTrue()
    {
        _ = typeof(System.Text.Json.JsonSerializerOptions).Assembly;

        Assert.True(SystemTextJsonCapabilities.WebPropertySupported());
    }
}
