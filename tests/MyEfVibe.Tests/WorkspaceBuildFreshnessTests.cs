using MyEfVibe.Workspace;

namespace MyEfVibe.Tests;

public sealed class WorkspaceBuildFreshnessTests
{
    [Fact]
    public void IsIsolatedOutputFresh_returns_true_when_dll_is_newer_than_sources()
    {
        var root = Path.Combine(Path.GetTempPath(), "efvibe-freshness-tests", Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "SampleApp");
        var sessionDirectory = Path.Combine(root, ".efvibe");
        Directory.CreateDirectory(projectDirectory);

        var projectPath = Path.Combine(projectDirectory, "SampleApp.csproj");
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>SampleApp</AssemblyName>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(projectDirectory, "Sample.cs"), "namespace SampleApp; public sealed class Sample { }");

        var output = WorkspaceBuilder.GetIsolatedBuildOutput(sessionDirectory, projectPath, "net10.0");
        var dllPath = Path.Combine(output.BaseOutputPath, "Release", "net10.0", "SampleApp.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(dllPath)!);
        File.WriteAllText(dllPath, "cached");
        File.SetLastWriteTimeUtc(dllPath, DateTime.UtcNow.AddMinutes(5));

        try
        {
            Assert.True(WorkspaceBuildFreshness.IsIsolatedOutputFresh(projectPath, "net10.0", output));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void IsIsolatedOutputFresh_returns_false_when_source_changes_after_dll()
    {
        var root = Path.Combine(Path.GetTempPath(), "efvibe-freshness-tests", Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "SampleApp");
        var sessionDirectory = Path.Combine(root, ".efvibe");
        Directory.CreateDirectory(projectDirectory);

        var projectPath = Path.Combine(projectDirectory, "SampleApp.csproj");
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>SampleApp</AssemblyName>
              </PropertyGroup>
            </Project>
            """);
        var sourcePath = Path.Combine(projectDirectory, "Sample.cs");
        File.WriteAllText(sourcePath, "namespace SampleApp; public sealed class Sample { }");

        var output = WorkspaceBuilder.GetIsolatedBuildOutput(sessionDirectory, projectPath, "net10.0");
        var dllPath = Path.Combine(output.BaseOutputPath, "Release", "net10.0", "SampleApp.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(dllPath)!);
        File.WriteAllText(dllPath, "cached");
        File.SetLastWriteTimeUtc(dllPath, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow);

        try
        {
            Assert.False(WorkspaceBuildFreshness.IsIsolatedOutputFresh(projectPath, "net10.0", output));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void EnsureProjectBuilt_skips_dotnet_build_when_output_is_fresh()
    {
        var root = Path.Combine(Path.GetTempPath(), "efvibe-freshness-tests", Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "SampleApp");
        var sessionDirectory = Path.Combine(root, ".efvibe");
        Directory.CreateDirectory(projectDirectory);

        var projectPath = Path.Combine(projectDirectory, "SampleApp.csproj");
        File.WriteAllText(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>SampleApp</AssemblyName>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(projectDirectory, "Sample.cs"), "namespace SampleApp; public sealed class Sample { }");

        var output = WorkspaceBuilder.GetIsolatedBuildOutput(sessionDirectory, projectPath, "net10.0");
        var dllPath = Path.Combine(output.BaseOutputPath, "Release", "net10.0", "SampleApp.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(dllPath)!);
        File.WriteAllText(dllPath, "cached");
        File.SetLastWriteTimeUtc(dllPath, DateTime.UtcNow.AddMinutes(5));

        try
        {
            var usedCache = WorkspaceBuilder.EnsureProjectBuilt(
                projectPath,
                "net10.0",
                output,
                WorkspaceBuildPolicy.Auto);

            Assert.True(usedCache);
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
