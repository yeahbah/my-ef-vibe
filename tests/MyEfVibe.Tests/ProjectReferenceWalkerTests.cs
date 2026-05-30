namespace MyEfVibe.Tests;

public sealed class ProjectReferenceWalkerTests
{
    [Fact]
    public void CollectScanProjectPaths_follows_windows_style_project_references_on_linux()
    {
        using var temp = new TempDirectory();
        var efProject = WriteProject(temp.Path, "Src/Persistence/Persistence.csproj");
        var startupProject = WriteProject(
            temp.Path,
            "Src/WebUI/WebUI.csproj",
            @"..\Infrastructure\Infrastructure.csproj");
        var infrastructureProject = WriteProject(
            temp.Path,
            "Src/Infrastructure/Infrastructure.csproj",
            @"..\Application\Application.csproj");
        var applicationProject = WriteProject(temp.Path, "Src/Application/Application.csproj");

        var projectPaths = LinqProjectSourceWalker.CollectScanProjectPaths(efProject, startupProject);

        Assert.Contains(efProject, projectPaths);
        Assert.Contains(startupProject, projectPaths);
        Assert.Contains(infrastructureProject, projectPaths);
        Assert.Contains(applicationProject, projectPaths);
    }

    private static string WriteProject(
        string root,
        string relativePath,
        params string[] projectReferences)
    {
        var projectPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);

        var references = string.Join(
            Environment.NewLine,
            projectReferences.Select(reference => $"""    <ProjectReference Include="{reference}" />"""));

        File.WriteAllText(
            projectPath,
            $$"""
              <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                  <TargetFramework>net10.0</TargetFramework>
                </PropertyGroup>
                <ItemGroup>
              {{references}}
                </ItemGroup>
              </Project>
              """);

        return projectPath;
    }

    private sealed class TempDirectory : IDisposable
    {
        internal TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "efvibe-tests",
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
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}