using System.Reflection;
using System.Runtime.Loader;

namespace MyEfVibe;

internal static class AssemblyResolutionHelpers
{
    internal static string GetCacheKey(AssemblyName assemblyName)
    {
        var name = assemblyName.Name ?? string.Empty;
        var version = assemblyName.Version;

        if (version is null || IsZeroVersion(version))
        {
            return name;
        }

        return FormattableString.Invariant($"{name}|{version}");
    }

    internal static bool IsZeroVersion(Version version)
    {
        return version is { Major: 0, Minor: 0, Build: 0, Revision: 0 };
    }

    internal static bool VersionMatches(AssemblyName requested, Assembly loaded)
    {
        var requestedVersion = requested.Version;

        if (requestedVersion is null || IsZeroVersion(requestedVersion))
        {
            return true;
        }

        return VersionsMatch(requestedVersion, loaded.GetName().Version);
    }

    internal static bool VersionsMatch(Version? requested, Version? candidate)
    {
        if (requested is null || candidate is null)
        {
            return requested == candidate;
        }

        if (requested == candidate)
        {
            return true;
        }

        return requested.Major == candidate.Major
               && requested.Minor == candidate.Minor
               && NormalizeBuild(requested) == NormalizeBuild(candidate)
               && NormalizeRevision(requested) == NormalizeRevision(candidate);
    }

    private static int NormalizeBuild(Version version)
    {
        return version.Build == -1 ? 0 : version.Build;
    }

    private static int NormalizeRevision(Version version)
    {
        return version.Revision == -1 ? 0 : version.Revision;
    }

    internal static Assembly? FindLoadedAssembly(AssemblyName requested)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, requested.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (VersionMatches(requested, assembly))
            {
                return assembly;
            }
        }

        return null;
    }

    /// <summary>
    ///     Returns an already-loaded assembly for <paramref name="absolutePath" /> when the same simple name
    ///     was loaded from another path (common when EF and startup projects both copy the same dependency).
    /// </summary>
    internal static Assembly? GetLoadedAssembly(AssemblyName requestedFromPath, string absolutePath)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.IsNullOrEmpty(assembly.Location))
            {
                continue;
            }

            if (string.Equals(assembly.Location, absolutePath, StringComparison.OrdinalIgnoreCase))
            {
                return assembly;
            }
        }

        var versionMatched = FindLoadedAssembly(requestedFromPath);

        if (versionMatched is not null)
        {
            return versionMatched;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, requestedFromPath.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (VersionMatches(requestedFromPath, assembly))
            {
                return assembly;
            }
        }

        // Default ALC cannot load the same assembly simple name twice (even from another path).
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(assembly.GetName().Name, requestedFromPath.Name, StringComparison.OrdinalIgnoreCase))
            {
                return assembly;
            }
        }

        return null;
    }

    internal static Assembly LoadFromPath(AssemblyLoadContext context, string absolutePath)
    {
        var assemblyName = AssemblyName.GetAssemblyName(absolutePath);

        var existing = GetLoadedAssembly(assemblyName, absolutePath);

        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return context.LoadFromAssemblyPath(absolutePath);
        }
        catch (FileLoadException)
        {
            var loaded = GetLoadedAssembly(assemblyName, absolutePath);

            if (loaded is not null)
            {
                return loaded;
            }

            throw;
        }
    }

    internal static bool IsCompatibleWithRequestedVersion(AssemblyName requested, string absolutePath)
    {
        var requestedVersion = requested.Version;

        if (requestedVersion is null || IsZeroVersion(requestedVersion))
        {
            return true;
        }

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