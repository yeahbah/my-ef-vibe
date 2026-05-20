using System.Reflection;

namespace MyEfVibe;

internal static class SystemTextJsonCapabilities
{
    internal const string AssemblySimpleName = "System.Text.Json";

    internal static bool WebPropertySupported()
        => WebPropertySupported(out _);

    internal static bool WebPropertySupported(out Assembly? loadedAssembly)
    {
        loadedAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(static assembly =>
                string.Equals(assembly.GetName().Name, AssemblySimpleName, StringComparison.OrdinalIgnoreCase));

        if (loadedAssembly is null)
            return false;

        return loadedAssembly
                   .GetType("System.Text.Json.JsonSerializerOptions")
                   ?.GetProperty("Web", BindingFlags.Public | BindingFlags.Static)
               is not null;
    }
}
