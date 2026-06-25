using MyEfVibe.Linq;

namespace MyEfVibe.Workspace;

internal static class WorkspaceBuildFreshness
{
    private static readonly string[] DirectoryBuildFileNames =
    [
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props"
    ];

    internal static bool IsIsolatedOutputFresh(
        string csprojFullPath,
        string targetFrameworkMoniker,
        ProjectBuildOutput isolatedOutput)
    {
        var assemblyName = CsprojReader.ReadLogicalAssemblyName(csprojFullPath);

        if (!WorkspaceBuildResult.TryLocateBuiltAssembly(
                isolatedOutput.BaseOutputPath,
                assemblyName,
                targetFrameworkMoniker,
                out var dllPath))
        {
            return false;
        }

        var outputUtc = File.GetLastWriteTimeUtc(dllPath);
        var newestInputUtc = GetNewestInputWriteTimeUtc(csprojFullPath);

        return newestInputUtc <= outputUtc;
    }

    internal static DateTime GetNewestInputWriteTimeUtc(string entryCsprojPath)
    {
        var newest = DateTime.MinValue;

        foreach (var projectPath in LinqProjectSourceWalker.CollectProjectPathsFromEntry(entryCsprojPath))
        {
            newest = MaxUtc(newest, File.GetLastWriteTimeUtc(projectPath));

            var projectDirectory = Path.GetDirectoryName(projectPath);

            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                continue;
            }

            IncludeDirectoryBuildFiles(projectDirectory, ref newest);
            IncludeProjectRestoreArtifacts(projectDirectory, ref newest);

            foreach (var sourceFile in LinqProjectSourceWalker.EnumerateSourceFiles(projectDirectory))
            {
                newest = MaxUtc(newest, File.GetLastWriteTimeUtc(sourceFile));
            }

            foreach (var propsFile in Directory.EnumerateFiles(
                         projectDirectory,
                         "*.props",
                         SearchOption.TopDirectoryOnly))
            {
                newest = MaxUtc(newest, File.GetLastWriteTimeUtc(propsFile));
            }

            foreach (var targetsFile in Directory.EnumerateFiles(
                         projectDirectory,
                         "*.targets",
                         SearchOption.TopDirectoryOnly))
            {
                newest = MaxUtc(newest, File.GetLastWriteTimeUtc(targetsFile));
            }
        }

        return newest;
    }

    private static void IncludeProjectRestoreArtifacts(string projectDirectory, ref DateTime newest)
    {
        foreach (var relativePath in new[]
                 {
                     Path.Combine("obj", "project.assets.json"),
                     "packages.lock.json",
                     "nuget.config"
                 })
        {
            var path = Path.Combine(projectDirectory, relativePath);

            if (File.Exists(path))
            {
                newest = MaxUtc(newest, File.GetLastWriteTimeUtc(path));
            }
        }
    }

    private static void IncludeDirectoryBuildFiles(string projectDirectory, ref DateTime newest)
    {
        var directory = projectDirectory;

        while (!string.IsNullOrEmpty(directory))
        {
            foreach (var fileName in DirectoryBuildFileNames)
            {
                var path = Path.Combine(directory, fileName);

                if (File.Exists(path))
                {
                    newest = MaxUtc(newest, File.GetLastWriteTimeUtc(path));
                }
            }

            directory = Directory.GetParent(directory)?.FullName;
        }
    }

    private static DateTime MaxUtc(DateTime left, DateTime right)
    {
        return left >= right ? left : right;
    }
}
