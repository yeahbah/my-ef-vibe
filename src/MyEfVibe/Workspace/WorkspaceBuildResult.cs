using System.Collections.Immutable;
using System.Globalization;

namespace MyEfVibe.Workspace;

internal sealed record WorkspaceBuildResult(
    string SessionDirectory,
    string ProjectPath,
    string StartupProjectPath,
    string OutputDirectory,
    string PrimaryAssemblyDll,
    string TargetFrameworkMoniker,
    ProjectBuildOutput ProjectBuildOutput,
    ProjectBuildOutput? StartupBuildOutput = null,
    string? StartupOutputDirectory = null)
{
    internal ImmutableHashSet<string> ReferenceAssemblyPaths =>
        WorkspaceReferenceCollector.Collect(OutputDirectory, PrimaryAssemblyDll);

    internal static WorkspaceBuildResult RequirePrimaryAssembly(
        string sessionDirectory,
        FileInfo csprojFile,
        FileInfo startupProject,
        string targetFrameworkMoniker,
        ProjectBuildOutput projectBuildOutput,
        ProjectBuildOutput? startupBuildOutput)
    {
        var asmNameLogical = CsprojReader.ReadLogicalAssemblyName(csprojFile.FullName);

        if (!TryLocateBuiltAssembly(
                projectBuildOutput.BaseOutputPath,
                asmNameLogical,
                targetFrameworkMoniker,
                out var dll))
        {
            throw new WorkspaceException(
                $"Could not find `{asmNameLogical}.dll` after build for `{targetFrameworkMoniker}`."
                + $" Checked isolated build output `{projectBuildOutput.BaseOutputPath}`.");
        }

        var outputDirectory =
            Path.GetDirectoryName(dll)!;

        string? startupOutputDirectory = null;

        if (!string.Equals(
                csprojFile.FullName,
                startupProject.FullName,
                StringComparison.OrdinalIgnoreCase)
            && startupBuildOutput is not null
            && TryLocateStartupOutput(startupProject.FullName, targetFrameworkMoniker, startupBuildOutput,
                out var locatedStartupOutput))
        {
            startupOutputDirectory = locatedStartupOutput;
        }

        return new WorkspaceBuildResult(
            Path.GetFullPath(sessionDirectory.TrimEnd(Path.DirectorySeparatorChar)),
            csprojFile.FullName,
            startupProject.FullName,
            outputDirectory,
            dll,
            targetFrameworkMoniker,
            projectBuildOutput,
            startupBuildOutput,
            startupOutputDirectory);
    }

    internal static bool TryLocateStartupOutput(
        string startupProjectPath,
        string targetFrameworkMoniker,
        ProjectBuildOutput startupBuildOutput,
        out string? outputDirectory)
    {
        outputDirectory = null;
        var startupAssemblyName = CsprojReader.ReadLogicalAssemblyName(startupProjectPath);

        if (!TryLocateBuiltAssembly(
                startupBuildOutput.BaseOutputPath,
                startupAssemblyName,
                targetFrameworkMoniker,
                out var startupDll))
        {
            return false;
        }

        outputDirectory = Path.GetDirectoryName(startupDll);

        return true;
    }

    internal static bool TryLocateBuiltAssembly(
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
            {
                continue;
            }

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
        {
            return decimal.Zero;
        }

        return decimal.TryParse(monikerFolderName.AsSpan()[3..], NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var parsedNetMonikerVersion)
            ? parsedNetMonikerVersion
            : decimal.Zero;
    }
}