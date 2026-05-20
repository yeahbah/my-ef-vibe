using System.Text.Json;

namespace MyEfVibe;

internal sealed class SharedFrameworkCatalog
{
    private readonly Dictionary<string, string> _assemblyNameToPath =
        new(StringComparer.OrdinalIgnoreCase);

    internal static SharedFrameworkCatalog Create(
        string targetFrameworkMoniker,
        string outputDirectory,
        string primaryAssemblyDll,
        string? startupOutputDirectory,
        string? startupAssemblyDll)
    {
        var catalog = new SharedFrameworkCatalog();

        catalog.IndexMicrosoftNetCoreAppFromMoniker(targetFrameworkMoniker);

        catalog.TryIndexFromRuntimeConfig(
            Path.Combine(
                outputDirectory,
                $"{Path.GetFileNameWithoutExtension(primaryAssemblyDll)}.runtimeconfig.json"));

        if (!string.IsNullOrEmpty(startupOutputDirectory)
            && !string.IsNullOrEmpty(startupAssemblyDll)
            && !string.Equals(startupOutputDirectory, outputDirectory, StringComparison.OrdinalIgnoreCase))
        {
            catalog.TryIndexFromRuntimeConfig(
                Path.Combine(
                    startupOutputDirectory,
                    $"{Path.GetFileNameWithoutExtension(startupAssemblyDll)}.runtimeconfig.json"));
        }

        return catalog;
    }

    private void IndexMicrosoftNetCoreAppFromMoniker(string targetFrameworkMoniker)
    {
        var normalized = ProjectTargetFrameworkResolver.NormalizeMoniker(targetFrameworkMoniker);

        if (!normalized.StartsWith("net", StringComparison.OrdinalIgnoreCase)
            || !decimal.TryParse(
                normalized.AsSpan()[3..],
                System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture,
                out var netMajor))
            return;

        var dotnetRoot = DotNetInstallRoot.Resolve();
        var sharedRoot = Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App");

        if (!Directory.Exists(sharedRoot))
            return;

        string? bestDirectory = null;
        Version? bestVersion = null;

        foreach (var frameworkDirectory in Directory.EnumerateDirectories(sharedRoot))
        {
            var folderName = Path.GetFileName(frameworkDirectory);

            if (!Version.TryParse(folderName, out var frameworkVersion)
                || frameworkVersion.Major != (int)netMajor)
                continue;

            if (bestVersion is null || frameworkVersion > bestVersion)
            {
                bestVersion = frameworkVersion;
                bestDirectory = frameworkDirectory;
            }
        }

        if (bestDirectory is not null)
            IndexFrameworkDirectory(bestDirectory);
    }

    private void TryIndexFromRuntimeConfig(string runtimeConfigPath)
    {
        if (!File.Exists(runtimeConfigPath))
            return;

        using var document = JsonDocument.Parse(File.ReadAllText(runtimeConfigPath));

        if (!document.RootElement.TryGetProperty("runtimeOptions", out var runtimeOptions))
            return;

        if (!runtimeOptions.TryGetProperty("frameworks", out var frameworks))
            return;

        var dotnetRoot = DotNetInstallRoot.Resolve();

        foreach (var framework in frameworks.EnumerateArray())
        {
            if (!framework.TryGetProperty("name", out var nameProperty)
                || !framework.TryGetProperty("version", out var versionProperty))
                continue;

            var frameworkName = nameProperty.GetString();

            var frameworkVersion = versionProperty.GetString();

            if (string.IsNullOrWhiteSpace(frameworkName) || string.IsNullOrWhiteSpace(frameworkVersion))
                continue;

            IndexFrameworkDirectory(
                Path.Combine(dotnetRoot, "shared", frameworkName, frameworkVersion));
        }
    }

    private void IndexFrameworkDirectory(string frameworkDirectory)
    {
        if (!Directory.Exists(frameworkDirectory))
            return;

        foreach (var dllPath in Directory.EnumerateFiles(frameworkDirectory, "*.dll"))
        {
            var assemblyName = Path.GetFileNameWithoutExtension(dllPath);

            _assemblyNameToPath[assemblyName] = dllPath;
        }
    }

    internal bool TryResolve(string assemblySimpleName, out string absolutePath)
        => _assemblyNameToPath.TryGetValue(assemblySimpleName, out absolutePath!);
}
