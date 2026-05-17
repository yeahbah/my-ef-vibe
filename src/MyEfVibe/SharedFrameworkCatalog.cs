using System.Text.Json;

namespace MyEfVibe;

internal sealed class SharedFrameworkCatalog
{
    private readonly Dictionary<string, string> _assemblyNameToPath =
        new(StringComparer.OrdinalIgnoreCase);

    internal static SharedFrameworkCatalog Load(string outputDirectory, string primaryAssemblyDll)
    {
        var catalog = new SharedFrameworkCatalog();

        var runtimeConfigPath = Path.Combine(
            outputDirectory,
            $"{Path.GetFileNameWithoutExtension(primaryAssemblyDll)}.runtimeconfig.json");

        if (!File.Exists(runtimeConfigPath))
            return catalog;

        using var document = JsonDocument.Parse(File.ReadAllText(runtimeConfigPath));

        if (!document.RootElement.TryGetProperty("runtimeOptions", out var runtimeOptions))
            return catalog;

        if (!runtimeOptions.TryGetProperty("frameworks", out var frameworks))
            return catalog;

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

            catalog.IndexFrameworkDirectory(
                Path.Combine(dotnetRoot, "shared", frameworkName, frameworkVersion));
        }

        return catalog;
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
