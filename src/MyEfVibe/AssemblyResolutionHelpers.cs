using System.Reflection;

namespace MyEfVibe;

internal static class AssemblyResolutionHelpers
{
    internal static string GetCacheKey(AssemblyName assemblyName)
    {
        var name = assemblyName.Name ?? string.Empty;
        var version = assemblyName.Version;

        if (version is null || IsZeroVersion(version))
            return name;

        return FormattableString.Invariant($"{name}|{version}");
    }

    internal static bool IsZeroVersion(Version version)
        => version.Major == 0 && version.Minor == 0 && version.Build == 0 && version.Revision == 0;

    internal static bool VersionMatches(AssemblyName requested, Assembly loaded)
    {
        var requestedVersion = requested.Version;

        if (requestedVersion is null || IsZeroVersion(requestedVersion))
            return true;

        return loaded.GetName().Version == requestedVersion;
    }

    internal static Assembly? FindLoadedAssembly(AssemblyName requested)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, requested.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (VersionMatches(requested, assembly))
                return assembly;
        }

        return null;
    }
}
