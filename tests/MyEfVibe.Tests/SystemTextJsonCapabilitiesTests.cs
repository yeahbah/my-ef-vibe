using System.Reflection;
using System.Text.Json;

namespace MyEfVibe.Tests;

public sealed class SystemTextJsonCapabilitiesTests
{
    [Fact]
    public void IsCompatibleLoaded_WhenFrameworkJsonIsLoaded_ReturnsTrue()
    {
        _ = typeof(JsonSerializerOptions).Assembly;

        Assert.True(SystemTextJsonCapabilities.IsCompatibleLoaded());
    }

    [Fact]
    public void IsSharedFrameworkAssembly_recognizes_netcore_shared_path()
    {
        var assembly = new SharedFrameworkAssemblyStub(
            "System.Text.Json",
            new Version(8, 0, 0, 0),
            "/usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.11/System.Text.Json.dll");

        Assert.True(SystemTextJsonCapabilities.IsSharedFrameworkAssembly(assembly));
        Assert.True(SystemTextJsonCapabilities.IsCompatible(assembly));
    }

    private sealed class SharedFrameworkAssemblyStub(string name, Version version, string location) : Assembly
    {
        public override string Location => location;

        public override AssemblyName GetName()
        {
            return new AssemblyName(name) { Version = version };
        }
    }
}