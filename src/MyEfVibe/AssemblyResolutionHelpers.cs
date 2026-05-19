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

        return VersionsMatch(requestedVersion, loaded.GetName().Version);
    }

    internal static bool VersionsMatch(Version? requested, Version? candidate)
    {
        if (requested is null || candidate is null)
            return requested == candidate;

        if (requested == candidate)
            return true;

        return requested.Major == candidate.Major
               && requested.Minor == candidate.Minor
               && NormalizeBuild(requested) == NormalizeBuild(candidate)
               && NormalizeRevision(requested) == NormalizeRevision(candidate);
    }

    private static int NormalizeBuild(Version version) => version.Build == -1 ? 0 : version.Build;

    private static int NormalizeRevision(Version version) => version.Revision == -1 ? 0 : version.Revision;

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

    internal static bool IsCompatibleWithRequestedVersion(AssemblyName requested, string absolutePath)
    {
        var requestedVersion = requested.Version;

        if (requestedVersion is null || IsZeroVersion(requestedVersion))
            return true;

        try
        {
            return VersionsMatch(requestedVersion, AssemblyName.GetAssemblyName(absolutePath).Version);
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
