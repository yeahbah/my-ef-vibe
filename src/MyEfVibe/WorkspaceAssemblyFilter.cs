namespace MyEfVibe;

internal static class WorkspaceAssemblyFilter
{
    private static readonly string[] ExcludedNamePrefixes =
    [
        "Microsoft.CodeAnalysis",
        "Microsoft.Build",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Design",
        "Humanizer",
        "Mono.TextTemplating",
        "myefvibe",
    ];

    private static readonly string[] ExcludedAssemblyNames =
    [
        "System.Diagnostics.DiagnosticSource",
    ];

    internal static bool ShouldScanAssembly(string dllPath)
    {
        var simpleName = Path.GetFileNameWithoutExtension(dllPath);

        if (string.IsNullOrEmpty(simpleName))
            return false;

        if (ExcludedAssemblyNames.Any(name =>
                string.Equals(simpleName, name, StringComparison.OrdinalIgnoreCase)))
            return false;

        return !ExcludedNamePrefixes.Any(prefix =>
            simpleName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
