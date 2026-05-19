using System.Collections.Immutable;
using System.Globalization;

namespace MyEfVibe;

internal sealed record WorkspaceBuildResult(
    string SessionDirectory,
    string ProjectPath,
    string StartupProjectPath,
    string OutputDirectory,
    string PrimaryAssemblyDll,
    string? StartupOutputDirectory = null)
{
    internal ImmutableHashSet<string> ReferenceAssemblyPaths =>
        WorkspaceReferenceCollector.Collect(OutputDirectory, PrimaryAssemblyDll);

    internal static WorkspaceBuildResult RequirePrimaryAssembly(
        string sessionDirectory,
        FileInfo csprojFile,
        FileInfo startupProject)
    {
        var projectDirectory =
            csprojFile.Directory!.FullName.TrimEnd(Path.DirectorySeparatorChar);

        var asmNameLogical = CsprojReader.ReadLogicalAssemblyName(csprojFile.FullName);

        if (!TryLocateBuiltAssembly(Path.Combine(projectDirectory, "bin"), asmNameLogical, out var dll))
            throw new WorkspaceException(
                $"Could not find `{asmNameLogical}.dll` after build. Checked `bin/Debug` and `bin/Release` for common TFMs.");

        var outputDirectory =
            Path.GetDirectoryName(dll)!;

        string? startupOutputDirectory = null;

        if (!string.Equals(
                csprojFile.FullName,
                startupProject.FullName,
                StringComparison.OrdinalIgnoreCase)
            && TryLocateStartupOutput(startupProject.FullName, out var locatedStartupOutput))
            startupOutputDirectory = locatedStartupOutput;

        return new WorkspaceBuildResult(
            SessionDirectory: Path.GetFullPath(sessionDirectory.TrimEnd(Path.DirectorySeparatorChar)),
            ProjectPath: csprojFile.FullName,
            StartupProjectPath: startupProject.FullName,
            OutputDirectory: outputDirectory,
            PrimaryAssemblyDll: dll,
            StartupOutputDirectory: startupOutputDirectory);
    }

    internal static bool TryLocateStartupOutput(string startupProjectPath, out string? outputDirectory)
    {
        outputDirectory = null;

        var startupProjectDirectory =
            Path.GetDirectoryName(startupProjectPath);

        if (string.IsNullOrEmpty(startupProjectDirectory))
            return false;

        var startupAssemblyName = CsprojReader.ReadLogicalAssemblyName(startupProjectPath);

        if (!TryLocateBuiltAssembly(Path.Combine(startupProjectDirectory, "bin"), startupAssemblyName, out var startupDll))
            return false;

        outputDirectory = Path.GetDirectoryName(startupDll);

        return true;
    }

    private static bool TryLocateBuiltAssembly(string binRoot, string assemblyName, out string dllPath)
    {
        var preferredTfm = HostRuntimeFramework.PreferredOutputFolderName();

        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var configurationRoot = Path.Combine(binRoot, configuration);

            if (!Directory.Exists(configurationRoot))
                continue;

            if (!string.IsNullOrEmpty(preferredTfm))
            {
                var preferredCandidate = Path.Combine(configurationRoot, preferredTfm, $"{assemblyName}.dll");

                if (File.Exists(preferredCandidate))
                {
                    dllPath = preferredCandidate;
                    return true;
                }
            }

            var tfmDirectoriesDescending =
                Directory.EnumerateDirectories(configurationRoot)
                    .OrderByDescending(static tfmPath => TfmRankingScore.DescendingScore(tfmPath));

            foreach (var tfmFolder in tfmDirectoriesDescending)
            {
                if (!string.IsNullOrEmpty(preferredTfm)
                    && string.Equals(
                        Path.GetFileName(tfmFolder),
                        preferredTfm,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                var candidate = Path.Combine(tfmFolder, $"{assemblyName}.dll");

                if (!File.Exists(candidate))
                    continue;

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
