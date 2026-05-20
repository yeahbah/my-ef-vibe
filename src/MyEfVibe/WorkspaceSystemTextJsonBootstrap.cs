using System.Reflection;
using System.Runtime.Loader;

namespace MyEfVibe;

internal static class WorkspaceSystemTextJsonBootstrap
{
    internal static void EnsureLoaded(WorkspaceAssemblyResolver resolver, SharedFrameworkCatalog sharedFrameworkCatalog)
    {
        if (SystemTextJsonCapabilities.TryGetLoaded(out var existing)
            && !SystemTextJsonCapabilities.IsCompatible(existing))
        {
            throw CreateIncompatibleAlreadyLoadedException(existing);
        }

        if (SystemTextJsonCapabilities.IsCompatibleLoaded())
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

            if (SystemTextJsonCapabilities.IsCompatibleLoaded())
                return;
        }

        if (SystemTextJsonCapabilities.IsCompatibleLoaded())
            return;

        if (SystemTextJsonCapabilities.TryGetLoaded(out var stillLoaded))
            throw CreateIncompatibleAlreadyLoadedException(stillLoaded);

        throw new InvalidOperationException(
            "Could not load a compatible `System.Text.Json` for the workspace."
            + $"{Environment.NewLine}Microsoft.Extensions and ASP.NET Core configuration require `JsonSerializerOptions.Web`."
            + $"{Environment.NewLine}Ensure the .NET SDK includes the target shared framework (for example `Microsoft.NETCore.App` for `net8.0`),"
            + $" rebuild with `-f net8.0`, and remove any stale `System.Text.Json.dll` copied into the project `bin` folder.");
    }

    private static InvalidOperationException CreateIncompatibleAlreadyLoadedException(Assembly assembly)
        => new(
            "An incompatible `System.Text.Json` is already loaded in this process"
            + $" ({SystemTextJsonCapabilities.Describe(assembly)})."
            + $"{Environment.NewLine}Microsoft.Extensions and ASP.NET Core configuration require `JsonSerializerOptions.Web`,"
            + $" which is missing from that copy."
            + $"{Environment.NewLine}efvibe does not unload assemblies after a session — exit the REPL/process and start a new `efvibe` run."
            + $"{Environment.NewLine}If this happens on the first launch, delete `System.Text.Json.dll` from the project `bin` folder,"
            + $" rebuild the `-p` / `-s` projects, and update/reinstall the efvibe global tool.");

    private static IEnumerable<string> EnumerateCandidatePaths(
        WorkspaceAssemblyResolver resolver,
        SharedFrameworkCatalog sharedFrameworkCatalog)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (sharedFrameworkCatalog.TryResolve(SystemTextJsonCapabilities.AssemblySimpleName, out var sharedPath)
            && seen.Add(sharedPath))
            yield return sharedPath;

        if (resolver.DepsManifest?.TryResolve(SystemTextJsonCapabilities.AssemblySimpleName, out var depsPath) == true
            && IsPackagePathMajorVersionAtLeastFive(depsPath)
            && seen.Add(depsPath))
            yield return depsPath;
    }

    private static bool IsPackagePathMajorVersionAtLeastFive(string absolutePath)
    {
        try
        {
            var version = System.Reflection.AssemblyName.GetAssemblyName(absolutePath).Version;

            return version is not null && version.Major >= 5;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch (FileLoadException)
        {
            return false;
        }
    }
}
