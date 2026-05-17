using System.Collections.Immutable;
using System.Globalization;

namespace MyEfVibe;

internal sealed record WorkspaceBuildResult(
    string WorkspaceDirectory,
    string ProjectPath,
    string OutputDirectory,
    string PrimaryAssemblyDll)
{
    internal ImmutableHashSet<string> ReferenceAssemblyPaths =>
        ArtifactResolver.CollectReferencePaths(OutputDirectory).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

    internal static WorkspaceBuildResult RequirePrimaryAssembly(string workspaceRootDirectory,
        FileInfo csprojFile)
    {
        var projectDirectory =
            csprojFile.Directory!.FullName.TrimEnd(Path.DirectorySeparatorChar);

        var asmNameLogical = CsprojReader.ReadLogicalAssemblyName(csprojFile.FullName);

        if (!TryLocateBuiltAssembly(Path.Combine(projectDirectory, "bin"), asmNameLogical, out var dll))
            throw new WorkspaceException(
                $"Could not find `{asmNameLogical}.dll` after build. Checked `bin/Debug` and `bin/Release` for common TFMs.");

        var outputDirectory =
            Path.GetDirectoryName(dll)!;

        return new WorkspaceBuildResult(
            WorkspaceDirectory: Path.GetFullPath(workspaceRootDirectory.TrimEnd(Path.DirectorySeparatorChar)),
            ProjectPath: csprojFile.FullName,
            OutputDirectory:
            outputDirectory,
            PrimaryAssemblyDll: dll);
    }

    private static bool TryLocateBuiltAssembly(string binRoot, string assemblyName, out string dllPath)
    {
        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var configurationRoot = Path.Combine(binRoot, configuration);

            if (!Directory.Exists(configurationRoot))
                continue;

            var tfmDirectoriesDescending =
                Directory.EnumerateDirectories(configurationRoot)
                    .OrderByDescending(static tfmPath => TfmRankingScore.DescendingScore(tfmPath));

            foreach (var tfmFolder in tfmDirectoriesDescending)
            {
                var candidate = Path.Combine(tfmFolder, $"{assemblyName}.dll");

                if (!File.Exists(candidate))
                    continue;

                dllPath =
                    candidate;

                return true;
            }
        }

        dllPath =
            string.Empty;

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
