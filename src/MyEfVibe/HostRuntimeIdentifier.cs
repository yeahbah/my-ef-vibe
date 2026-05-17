using System.Runtime.InteropServices;

namespace MyEfVibe;

internal static class HostRuntimeIdentifier
{
    internal static IReadOnlyList<string> GetRuntimeFallbacks()
    {
        var fallbacks = new List<string>();
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x64",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            _ => "x64",
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            fallbacks.Add($"osx-{architecture}");
            fallbacks.Add("osx");
            fallbacks.Add("unix");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            fallbacks.Add($"linux-{architecture}");
            fallbacks.Add("linux");
            fallbacks.Add("unix");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            fallbacks.Add($"win-{architecture}");
            fallbacks.Add("win");
        }

        fallbacks.Add("any");

        return fallbacks;
    }

    internal static int GetFallbackRank(string runtimeIdentifier, IReadOnlyList<string> fallbacks)
    {
        for (var index = 0; index < fallbacks.Count; index++)
        {
            if (string.Equals(fallbacks[index], runtimeIdentifier, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return int.MaxValue;
    }
}
