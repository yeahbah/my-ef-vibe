using System.Collections.Immutable;
using System.Globalization;

namespace MyEfVibe;

internal sealed record WorkspaceBuildResult(
    string SessionDirectory,
    string ProjectPath,
    string StartupProjectPath,
    string OutputDirectory,
    string PrimaryAssemblyDll,
    string TargetFrameworkMoniker,
    string? StartupOutputDirectory = null)
{
    internal ImmutableHashSet<string> ReferenceAssemblyPaths =>
        WorkspaceReferenceCollector.Collect(OutputDirectory, PrimaryAssemblyDll);

    internal static WorkspaceBuildResult RequirePrimaryAssembly(
        string sessionDirectory,
        FileInfo csprojFile,
        FileInfo startupProject,
        string targetFrameworkMoniker)
    {
        var projectDirectory =
            csprojFile.Directory!.FullName.TrimEnd(Path.DirectorySeparatorChar);

        var asmNameLogical = CsprojReader.ReadLogicalAssemblyName(csprojFile.FullName);

        if (!TryLocateBuiltAssembly(
                Path.Combine(projectDirectory, "bin"),
                asmNameLogical,
                targetFrameworkMoniker,
                out var dll))
            throw new WorkspaceException(
                $"Could not find `{asmNameLogical}.dll` after build for `{targetFrameworkMoniker}`."
                + $" Checked `bin/Debug` and `bin/Release`.");

        var outputDirectory =
            Path.GetDirectoryName(dll)!;

        string? startupOutputDirectory = null;

        if (!string.Equals(
                csprojFile.FullName,
                startupProject.FullName,
                StringComparison.OrdinalIgnoreCase)
            && TryLocateStartupOutput(startupProject.FullName, targetFrameworkMoniker, out var locatedStartupOutput))
            startupOutputDirectory = locatedStartupOutput;

        return new WorkspaceBuildResult(
            SessionDirectory: Path.GetFullPath(sessionDirectory.TrimEnd(Path.DirectorySeparatorChar)),
            ProjectPath: csprojFile.FullName,
            StartupProjectPath: startupProject.FullName,
            OutputDirectory: outputDirectory,
            PrimaryAssemblyDll: dll,
            TargetFrameworkMoniker: targetFrameworkMoniker,
            StartupOutputDirectory: startupOutputDirectory);
    }

    internal static bool TryLocateStartupOutput(
        string startupProjectPath,
        string targetFrameworkMoniker,
        out string? outputDirectory)
    {
        outputDirectory = null;

        var startupProjectDirectory =
            Path.GetDirectoryName(startupProjectPath);

        if (string.IsNullOrEmpty(startupProjectDirectory))
            return false;

        var startupAssemblyName = CsprojReader.ReadLogicalAssemblyName(startupProjectPath);

        if (!TryLocateBuiltAssembly(
                Path.Combine(startupProjectDirectory, "bin"),
                startupAssemblyName,
                targetFrameworkMoniker,
                out var startupDll))
            return false;

        outputDirectory = Path.GetDirectoryName(startupDll);

        return true;
    }

    private static bool TryLocateBuiltAssembly(
        string binRoot,
        string assemblyName,
        string targetFrameworkMoniker,
        out string dllPath)
    {
        var tfm = ProjectTargetFrameworkResolver.NormalizeMoniker(targetFrameworkMoniker);

        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var configurationRoot = Path.Combine(binRoot, configuration);

            if (!Directory.Exists(configurationRoot))
                continue;

            var candidate = Path.Combine(configurationRoot, tfm, $"{assemblyName}.dll");

            if (File.Exists(candidate))
            {
                dllPath = candidate;
                return true;
            }
        }

        dllPath = string.Empty;

        return false;
    }
}

internal static class TfmRankingScore
{
    internal static decimal DescendingScore(string tfmAbsolutePath)
    {
        var monikerFolderName = Path.GetFileName(tfmAbsolutePath.TrimEnd(Path.DirectorySeparatorChar));

        if (!monikerFolderName.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            return decimal.Zero;

        return decimal.TryParse(monikerFolderName.AsSpan()[3..], NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var parsedNetMonikerVersion)
            ? parsedNetMonikerVersion
            : decimal.Zero;

    }

}
