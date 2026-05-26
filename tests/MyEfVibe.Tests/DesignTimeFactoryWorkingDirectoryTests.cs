namespace MyEfVibe.Tests;

public sealed class DesignTimeFactoryWorkingDirectoryTests
{
    [Fact]
    public void ResolveDesignTimeFactoryWorkingDirectory_uses_ef_project_when_factory_assembly_matches_project()
    {
        using var temp = new TempDirectory();
        var projectPath = WriteProject(temp.Path, "Src/Persistence/Persistence.csproj", FactoryAssemblyName);
        var startupProjectPath = WriteProject(temp.Path, "Src/WebUI/WebUI.csproj", "Northwind.WebUI");

        var workingDirectory = DbContextActivator.ResolveDesignTimeFactoryWorkingDirectory(
            typeof(DesignTimeFactoryMarker),
            projectPath,
            startupProjectPath);

        Assert.Equal(Path.GetDirectoryName(projectPath), workingDirectory);
    }

    [Fact]
    public void ResolveDesignTimeFactoryWorkingDirectory_uses_startup_project_when_factory_assembly_matches_startup()
    {
        using var temp = new TempDirectory();
        var projectPath = WriteProject(temp.Path, "Src/Persistence/Persistence.csproj", "Northwind.Persistence");
        var startupProjectPath = WriteProject(temp.Path, "Src/WebUI/WebUI.csproj", FactoryAssemblyName);

        var workingDirectory = DbContextActivator.ResolveDesignTimeFactoryWorkingDirectory(
            typeof(DesignTimeFactoryMarker),
            projectPath,
            startupProjectPath);

        Assert.Equal(Path.GetDirectoryName(startupProjectPath), workingDirectory);
    }

    [Fact]
    public void ResolveDesignTimeFactoryWorkingDirectory_falls_back_to_startup_project()
    {
        using var temp = new TempDirectory();
        var projectPath = WriteProject(temp.Path, "Src/Persistence/Persistence.csproj", "Northwind.Persistence");
        var startupProjectPath = WriteProject(temp.Path, "Src/WebUI/WebUI.csproj", "Northwind.WebUI");

        var workingDirectory = DbContextActivator.ResolveDesignTimeFactoryWorkingDirectory(
            typeof(DesignTimeFactoryMarker),
            projectPath,
            startupProjectPath);

        Assert.Equal(Path.GetDirectoryName(startupProjectPath), workingDirectory);
    }

    private static string FactoryAssemblyName => typeof(DesignTimeFactoryMarker).Assembly.GetName().Name!;

    private static string WriteProject(string root, string relativePath, string assemblyName)
    {
        var projectPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        File.WriteAllText(
            projectPath,
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <AssemblyName>{{assemblyName}}</AssemblyName>
              </PropertyGroup>
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
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class DesignTimeFactoryMarker;
}
