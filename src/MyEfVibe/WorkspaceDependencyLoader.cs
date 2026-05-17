using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace MyEfVibe;

internal static class WorkspaceDependencyLoader
{
    internal static void PreloadIntoDefaultContext(AssemblyDependencyResolver resolver, string entryAssemblyPath)
    {
        var outputDirectory = Path.GetDirectoryName(entryAssemblyPath)!;

        var depsPath = Path.Combine(
            outputDirectory,
            $"{Path.GetFileNameWithoutExtension(entryAssemblyPath)}.deps.json");

        if (!File.Exists(depsPath))
            return;

        using var document = JsonDocument.Parse(File.ReadAllText(depsPath));

        if (!document.RootElement.TryGetProperty("runtimeTarget", out var runtimeTargetProperty))
            return;

        if (!document.RootElement.TryGetProperty("targets", out var targets))
            return;

        if (!runtimeTargetProperty.TryGetProperty("name", out var runtimeTargetNameProperty))
            return;

        var runtimeTargetName = runtimeTargetNameProperty.GetString();

        if (string.IsNullOrWhiteSpace(runtimeTargetName)
            || !targets.TryGetProperty(runtimeTargetName, out var targetNode))
            return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assemblySimpleName in EnumerateRuntimeAssemblySimpleNames(targetNode))
        {
            if (!seen.Add(assemblySimpleName))
                continue;

            if (IsDesignTimeOrToolingAssembly(assemblySimpleName))
                continue;

            var resolvedPath = resolver.ResolveAssemblyToPath(new AssemblyName(assemblySimpleName));

            if (resolvedPath is null)
                continue;

            TryLoadIntoDefault(resolvedPath);
        }
    }

    private static IEnumerable<string> EnumerateRuntimeAssemblySimpleNames(JsonElement targetNode)
    {
        foreach (var library in targetNode.EnumerateObject())
        {
            if (!library.Value.TryGetProperty("runtime", out var runtimeAssets))
                continue;

            foreach (var runtimeAsset in runtimeAssets.EnumerateObject())
            {
                var fileName = Path.GetFileName(runtimeAsset.Name);

                if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return Path.GetFileNameWithoutExtension(fileName);
            }
        }
    }

    private static bool IsDesignTimeOrToolingAssembly(string assemblySimpleName)
        =>
            assemblySimpleName.StartsWith("Microsoft.Build", StringComparison.OrdinalIgnoreCase)
            || assemblySimpleName.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)
            || assemblySimpleName.StartsWith("Microsoft.EntityFrameworkCore.Design", StringComparison.OrdinalIgnoreCase)
            || assemblySimpleName.StartsWith("Humanizer", StringComparison.OrdinalIgnoreCase)
            || assemblySimpleName.StartsWith("Mono.TextTemplating", StringComparison.OrdinalIgnoreCase);

    private static void TryLoadIntoDefault(string absolutePath)
    {
        try
        {
            AssemblyLoadContext.Default.LoadFromAssemblyPath(absolutePath);
        }
        catch (BadImageFormatException)
        {
        }
        catch (FileLoadException)
        {
        }
    }
}
