using System.Reflection;
using System.Runtime.Versioning;

namespace MyEfVibe;

internal static class HostRuntimeFramework
{
    internal static string? PreferredOutputFolderName()
    {
        var targetFramework = typeof(HostRuntimeFramework).Assembly
            .GetCustomAttribute<TargetFrameworkAttribute>()
            ?.FrameworkName;

        if (string.IsNullOrWhiteSpace(targetFramework))
            return null;

        var frameworkName = new FrameworkName(targetFramework);

        if (!string.Equals(frameworkName.Identifier, ".NETCoreApp", StringComparison.OrdinalIgnoreCase))
            return null;

        return FormattableString.Invariant($"net{frameworkName.Version.Major}.{frameworkName.Version.Minor}");
    }
}
