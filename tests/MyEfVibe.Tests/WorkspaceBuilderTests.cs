namespace MyEfVibe.Tests;

public sealed class WorkspaceBuilderTests
{
    [Fact]
    public void GetIsolatedBuildOutput_uses_session_build_directory()
    {
        var sessionDirectory = Path.Combine(Path.GetTempPath(), "efvibe-tests", Guid.NewGuid().ToString("N"));
        var projectPath = Path.Combine(Path.GetTempPath(), "Sample.Project", "Sample.Project.csproj");

        var output = WorkspaceBuilder.GetIsolatedBuildOutput(sessionDirectory, projectPath, "net10.0");

        Assert.StartsWith(Path.Combine(sessionDirectory, ".build"), output.BaseOutputPath);
        Assert.DoesNotContain(Path.Combine("Sample.Project", "bin"), output.BaseOutputPath);
        Assert.EndsWith(Path.Combine("net10.0", "bin"), output.BaseOutputPath.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void BuildResolvedProject_loads_from_isolated_output_not_project_bin()
    {
        var root = Path.Combine(Path.GetTempPath(), "efvibe-build-tests", Guid.NewGuid().ToString("N"));
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
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(projectDirectory, "Sample.cs"),
            "namespace SampleApp; public sealed class Sample { }");

        try
        {
            var result = WorkspaceBuilder.BuildResolvedProject(
                sessionDirectory,
                new FileInfo(projectPath),
                new FileInfo(projectPath),
                "net10.0");

            Assert.True(File.Exists(result.PrimaryAssemblyDll));
            Assert.StartsWith(Path.Combine(sessionDirectory, ".build"), result.PrimaryAssemblyDll);
            Assert.False(Directory.Exists(Path.Combine(projectDirectory, "bin")));
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
    public void BuildResolvedProject_builds_startup_project_to_isolated_output()
    {
        var root = Path.Combine(Path.GetTempPath(), "efvibe-startup-build-tests", Guid.NewGuid().ToString("N"));
        var efProjectDirectory = Path.Combine(root, "Sample.Persistence");
        var startupProjectDirectory = Path.Combine(root, "Sample.Api");
        var sessionDirectory = Path.Combine(root, ".efvibe");
        Directory.CreateDirectory(efProjectDirectory);
        Directory.CreateDirectory(startupProjectDirectory);

        var efProjectPath = Path.Combine(efProjectDirectory, "Sample.Persistence.csproj");
        File.WriteAllText(
            efProjectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(efProjectDirectory, "SampleContext.cs"),
            "namespace Sample.Persistence; public sealed class SampleContext { }");

        var startupProjectPath = Path.Combine(startupProjectDirectory, "Sample.Api.csproj");
        File.WriteAllText(
            startupProjectPath,
            $$"""
              <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                  <OutputType>Exe</OutputType>
                  <TargetFramework>net10.0</TargetFramework>
                  <ImplicitUsings>enable</ImplicitUsings>
                  <Nullable>enable</Nullable>
                </PropertyGroup>
                <ItemGroup>
                  <ProjectReference Include="{{efProjectPath}}" />
                </ItemGroup>
              </Project>
              """);
        File.WriteAllText(Path.Combine(startupProjectDirectory, "Program.cs"),
            "Console.WriteLine(typeof(Sample.Persistence.SampleContext).FullName);");

        try
        {
            var result = WorkspaceBuilder.BuildResolvedProject(
                sessionDirectory,
                new FileInfo(efProjectPath),
                new FileInfo(startupProjectPath),
                "net10.0");

            Assert.NotNull(result.StartupOutputDirectory);
            Assert.StartsWith(Path.Combine(sessionDirectory, ".build"), result.PrimaryAssemblyDll);
            Assert.StartsWith(Path.Combine(sessionDirectory, ".build"), result.StartupOutputDirectory);
            Assert.False(Directory.Exists(Path.Combine(efProjectDirectory, "bin")));
            Assert.False(Directory.Exists(Path.Combine(startupProjectDirectory, "bin")));
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