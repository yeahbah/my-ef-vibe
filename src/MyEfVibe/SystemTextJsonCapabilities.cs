using System.Reflection;

namespace MyEfVibe;

internal static class SystemTextJsonCapabilities
{
    internal const string AssemblySimpleName = "System.Text.Json";

    internal static bool IsCompatibleLoaded()
        => TryGetLoaded(out var assembly) && IsCompatible(assembly);

    internal static bool TryGetLoaded(out Assembly? loadedAssembly)
    {
        loadedAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(static assembly =>
                string.Equals(assembly.GetName().Name, AssemblySimpleName, StringComparison.OrdinalIgnoreCase));

        return loadedAssembly is not null;
    }

    internal static bool IsCompatible(Assembly assembly)
    {
        if (assembly.GetName().Version is { Major: < 5 })
            return false;

        return assembly
                   .GetType("System.Text.Json.JsonSerializerOptions")
                   ?.GetProperty("Web", BindingFlags.Public | BindingFlags.Static)
               is not null;
    }

    internal static string Describe(Assembly assembly)
    {
        var version = assembly.GetName().Version?.ToString() ?? "unknown";
        var location = string.IsNullOrEmpty(assembly.Location) ? "(no path)" : assembly.Location;

        return $"version {version} at {location}";
    }
}
