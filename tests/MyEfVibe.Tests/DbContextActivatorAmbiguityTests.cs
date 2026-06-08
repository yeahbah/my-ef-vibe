using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class DbContextActivatorAmbiguityTests
{
    [Fact]
    public void ResolveInstance_reports_ambiguous_providers_when_ef_project_references_multiple_packages()
    {
        ConfigurationFallbackProbeDbContext.ParameterlessConstructorCalls = 0;

        using var temp = new TempDirectory();
        var project = Path.Combine(temp.Path, "Startup.csproj");
        File.WriteAllText(
            project,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.7" />
                <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(temp.Path, "appsettings.Development.json"),
            """
            {
              "ConnectionStrings": {
                "DefaultConnection": "Host=localhost;Database=test"
              }
            }
            """);

        var assemblyPath = typeof(ConfigurationFallbackProbeDbContext).Assembly.Location;
        var outputDirectory = Path.GetDirectoryName(assemblyPath)!;
        var workspaceBuild = new WorkspaceBuildResult(
            Path.Combine(temp.Path, ".efvibe"),
            project,
            project,
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
        Assert.Contains("Microsoft.EntityFrameworkCore.SqlServer", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Npgsql.EntityFrameworkCore.PostgreSQL", failure.Message, StringComparison.Ordinal);
        Assert.Contains("--provider", failure.Message, StringComparison.Ordinal);
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
