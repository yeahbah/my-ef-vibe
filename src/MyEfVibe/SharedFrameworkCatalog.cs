using System.Globalization;

namespace MyEfVibe;

internal sealed class SharedFrameworkCatalog
{
    private readonly Dictionary<string, string> _assemblyNameToPath =
        new(StringComparer.OrdinalIgnoreCase);

    internal static SharedFrameworkCatalog Create(string targetFrameworkMoniker)
    {
        var catalog = new SharedFrameworkCatalog();
        catalog.IndexMicrosoftNetCoreAppFromMoniker(targetFrameworkMoniker);

        return catalog;
    }

    internal void IndexProjectRuntimeConfigs(
        string outputDirectory,
        string primaryAssemblyDll,
        string? startupOutputDirectory,
        string? startupAssemblyDll)
    {
        TryIndexFromRuntimeConfig(
            Path.Combine(
                outputDirectory,
                $"{Path.GetFileNameWithoutExtension(primaryAssemblyDll)}.runtimeconfig.json"));

        if (!string.IsNullOrEmpty(startupOutputDirectory)
            && !string.IsNullOrEmpty(startupAssemblyDll)
            && !string.Equals(startupOutputDirectory, outputDirectory, StringComparison.OrdinalIgnoreCase))
        {
            TryIndexFromRuntimeConfig(
                Path.Combine(
                    startupOutputDirectory,
                    $"{Path.GetFileNameWithoutExtension(startupAssemblyDll)}.runtimeconfig.json"));
        }
    }

    private void IndexMicrosoftNetCoreAppFromMoniker(string targetFrameworkMoniker)
    {
        var normalized = ProjectTargetFrameworkResolver.NormalizeMoniker(targetFrameworkMoniker);

        if (!normalized.StartsWith("net", StringComparison.OrdinalIgnoreCase)
            || !decimal.TryParse(
                normalized.AsSpan()[3..],
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var netMajor))
        {
            return;
        }

        var dotnetRoot = DotNetInstallRoot.Resolve();
        var sharedRoot = Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App");

        if (!Directory.Exists(sharedRoot))
        {
            return;
        }

        string? bestDirectory = null;
        Version? bestVersion = null;

        foreach (var frameworkDirectory in Directory.EnumerateDirectories(sharedRoot))
        {
            var folderName = Path.GetFileName(frameworkDirectory);

            if (!Version.TryParse(folderName, out var frameworkVersion)
                || frameworkVersion.Major != (int)netMajor)
            {
                continue;
            }

            if (bestVersion is null || frameworkVersion > bestVersion)
            {
                bestVersion = frameworkVersion;
                bestDirectory = frameworkDirectory;
            }
        }

        if (bestDirectory is not null)
        {
            IndexFrameworkDirectory(bestDirectory);
        }
    }

    private void TryIndexFromRuntimeConfig(string runtimeConfigPath)
    {
        foreach (var framework in RuntimeFrameworkConfigParser.ReadFrameworks(runtimeConfigPath))
        {
            var frameworkDirectory = Path.Combine(
                DotNetInstallRoot.Resolve(),
                "shared",
                framework.Name,
                framework.Version);

            IndexFrameworkDirectory(frameworkDirectory);
        }
    }

    internal static string ResolveInstalledFrameworkDirectory(string requestedFrameworkDirectory)
    {
        if (Directory.Exists(requestedFrameworkDirectory))
        {
            return requestedFrameworkDirectory;
        }

        var parentDirectory = Path.GetDirectoryName(requestedFrameworkDirectory);
        var requestedVersionText = Path.GetFileName(requestedFrameworkDirectory);

        if (parentDirectory is null
            || !Version.TryParse(requestedVersionText, out var requestedVersion)
            || !Directory.Exists(parentDirectory))
        {
            return requestedFrameworkDirectory;
        }

        string? bestDirectory = null;
        Version? bestVersion = null;

        foreach (var candidateDirectory in Directory.EnumerateDirectories(parentDirectory))
        {
            var candidateVersionText = Path.GetFileName(candidateDirectory);

            if (!Version.TryParse(candidateVersionText, out var candidateVersion))
            {
                continue;
            }

            if (candidateVersion.Major != requestedVersion.Major
                || candidateVersion.Minor < requestedVersion.Minor)
            {
                continue;
            }

            if (bestVersion is null || candidateVersion > bestVersion)
            {
                bestVersion = candidateVersion;
                bestDirectory = candidateDirectory;
            }
        }

        return bestDirectory ?? requestedFrameworkDirectory;
    }

    private void IndexFrameworkDirectory(string frameworkDirectory)
    {
        frameworkDirectory = ResolveInstalledFrameworkDirectory(frameworkDirectory);

        if (!Directory.Exists(frameworkDirectory))
        {
            return;
        }

        foreach (var dllPath in Directory.EnumerateFiles(frameworkDirectory, "*.dll"))
        {
            var assemblyName = Path.GetFileNameWithoutExtension(dllPath);

            _assemblyNameToPath[assemblyName] = dllPath;
        }
    }

    internal bool TryResolve(string assemblySimpleName, out string absolutePath)
    {
        return _assemblyNameToPath.TryGetValue(assemblySimpleName, out absolutePath!);
    }
}