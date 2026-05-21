using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace MyEfVibe;

internal static class SystemTextJsonCapabilities
{
    internal const string AssemblySimpleName = "System.Text.Json";

    internal static bool IsCompatibleLoaded()
        => TryGetLoaded(out var assembly) && IsCompatible(assembly);

    internal static bool TryGetLoaded([NotNullWhen(true)] out Assembly? loadedAssembly)
    {
        Assembly? compatible = null;
        Assembly? fallback = null;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, AssemblySimpleName, StringComparison.OrdinalIgnoreCase))
                continue;

            fallback ??= assembly;

            if (IsCompatible(assembly))
            {
                compatible = assembly;
                break;
            }
        }

        loadedAssembly = compatible ?? fallback;

        return loadedAssembly is not null;
    }

    internal static bool IsCompatible(Assembly assembly)
    {
        if (assembly.GetName().Version is { Major: < 5 })
            return false;

        if (IsSharedFrameworkAssembly(assembly))
            return true;

        return HasJsonSerializerOptionsWeb(assembly);
    }

    internal static bool IsSharedFrameworkAssembly(Assembly assembly)
    {
        if (string.IsNullOrEmpty(assembly.Location))
            return false;

        var normalized = assembly.Location.Replace('\\', Path.DirectorySeparatorChar);

        return normalized.Contains(
                   $"{Path.DirectorySeparatorChar}shared{Path.DirectorySeparatorChar}Microsoft.NETCore.App{Path.DirectorySeparatorChar}",
                   StringComparison.OrdinalIgnoreCase)
               || normalized.Contains(
                   $"{Path.DirectorySeparatorChar}shared{Path.DirectorySeparatorChar}Microsoft.AspNetCore.App{Path.DirectorySeparatorChar}",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasJsonSerializerOptionsWeb(Assembly assembly)
    {
        var optionsType = assembly.GetType(
            "System.Text.Json.JsonSerializerOptions",
            throwOnError: false,
            ignoreCase: false);

        if (optionsType?.GetProperty("Web", BindingFlags.Public | BindingFlags.Static) is not null)
            return true;

        var qualifiedName = $"System.Text.Json.JsonSerializerOptions, {assembly.FullName}";

        optionsType = Type.GetType(qualifiedName, throwOnError: false);

        return optionsType?.GetProperty("Web", BindingFlags.Public | BindingFlags.Static) is not null;
    }

    internal static string Describe(Assembly assembly)
    {
        var version = assembly.GetName().Version?.ToString() ?? "unknown";
        var location = string.IsNullOrEmpty(assembly.Location) ? "(no path)" : assembly.Location;

        return $"version {version} at {location}";
    }
}
