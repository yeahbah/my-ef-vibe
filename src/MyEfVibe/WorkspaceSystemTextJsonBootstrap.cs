using System.Reflection;
using System.Runtime.Loader;

namespace MyEfVibe;

internal static class WorkspaceSystemTextJsonBootstrap
{
    internal static void EnsureLoaded(WorkspaceAssemblyResolver resolver, SharedFrameworkCatalog sharedFrameworkCatalog)
    {
        if (SystemTextJsonCapabilities.WebPropertySupported())
            return;

        foreach (var candidatePath in EnumerateCandidatePaths(resolver, sharedFrameworkCatalog))
        {
            try
            {
                AssemblyResolutionHelpers.LoadFromPath(AssemblyLoadContext.Default, candidatePath);
            }
            catch (Exception failure) when (failure is BadImageFormatException or FileLoadException or IOException)
            {
                continue;
            }

            if (SystemTextJsonCapabilities.WebPropertySupported())
                return;
        }

        if (SystemTextJsonCapabilities.WebPropertySupported())
            return;

        var loadedVersion = SystemTextJsonCapabilities.WebPropertySupported(out var loaded)
            ? loaded?.GetName().Version?.ToString()
            : null;

        throw new InvalidOperationException(
            "Could not load a compatible `System.Text.Json` for the workspace."
            + $"{Environment.NewLine}Microsoft.Extensions and ASP.NET Core configuration require `JsonSerializerOptions.Web`,"
            + $" which is missing from the copy already in memory"
            + (loadedVersion is null ? "." : $" (version {loadedVersion}).")
            + $"{Environment.NewLine}This usually means an older `System.Text.Json` package was loaded from the project output"
            + $" before the target framework copy."
            + $"{Environment.NewLine}Rebuild the `-p` / `-s` projects for the requested framework (`-f net8.0`), update efvibe,"
            + $" or remove stale `System.Text.Json.dll` from the project `bin` folder.");
    }

    private static IEnumerable<string> EnumerateCandidatePaths(
        WorkspaceAssemblyResolver resolver,
        SharedFrameworkCatalog sharedFrameworkCatalog)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (sharedFrameworkCatalog.TryResolve(SystemTextJsonCapabilities.AssemblySimpleName, out var sharedPath)
            && seen.Add(sharedPath))
            yield return sharedPath;

        if (resolver.DepsManifest?.TryResolve(SystemTextJsonCapabilities.AssemblySimpleName, out var depsPath) == true
            && seen.Add(depsPath))
            yield return depsPath;
    }
}
