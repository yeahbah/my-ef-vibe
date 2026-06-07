using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class DbContextActivatorConfigurationTests
{
    [Fact]
    public void ResolveInstance_does_not_fallback_to_parameterless_when_appsettings_connection_was_found()
    {
        ConfigurationFallbackProbeDbContext.ParameterlessConstructorCalls = 0;

        using var temp = new TempDirectory();
        var startupProject = Path.Combine(temp.Path, "Startup.csproj");

        File.WriteAllText(startupProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(
            Path.Combine(temp.Path, "appsettings.Development.json"),
            """
            {
              "ConnectionStrings": {
                "DefaultConnection": "Data Source=localhost,1533;User ID=wwi;Password=secret;Initial Catalog=WideWorldImporters;TrustServerCertificate=Yes"
              }
            }
            """);

        var assemblyPath = typeof(ConfigurationFallbackProbeDbContext).Assembly.Location;
        var outputDirectory = Path.GetDirectoryName(assemblyPath)!;
        var workspaceBuild = new WorkspaceBuildResult(
            Path.Combine(temp.Path, ".efvibe"),
            startupProject,
            startupProject,
            outputDirectory,
            assemblyPath,
            "net10.0",
            new ProjectBuildOutput(outputDirectory));

        using var host = WorkspaceHost.Load(workspaceBuild);

        var failure = Assert.Throws<InvalidOperationException>(() =>
            DbContextActivator.ResolveInstance(
                host,
                typeof(ConfigurationFallbackProbeDbContext).FullName,
                null,
                null,
                false));

        Assert.Equal(0, ConfigurationFallbackProbeDbContext.ParameterlessConstructorCalls);
        Assert.Contains("Configuration was read from the startup project", failure.Message, StringComparison.Ordinal);
    }

    private sealed class TempDirectory : IDisposable
    {
        internal TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "myefvibe-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch
            {
                // best effort
            }
        }
    }
}

public sealed class ConfigurationFallbackProbeDbContext : DbContext
{
    public ConfigurationFallbackProbeDbContext()
    {
        ParameterlessConstructorCalls++;
    }

    public ConfigurationFallbackProbeDbContext(DbContextOptions<ConfigurationFallbackProbeDbContext> options)
        : base(options)
    {
    }

    public static int ParameterlessConstructorCalls { get; set; }
}
