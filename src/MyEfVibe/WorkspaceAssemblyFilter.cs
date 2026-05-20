namespace MyEfVibe;

internal static class WorkspaceAssemblyFilter
{
    private static readonly string[] ExcludedNamePrefixes =
    [
        "Microsoft.CodeAnalysis",
        "Microsoft.Build",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Design",
        "Microsoft.Extensions.",
        "Humanizer",
        "Mono.TextTemplating",
        "myefvibe",
    ];

    private static readonly string[] ExcludedAssemblyNames =
    [
        "System.Diagnostics.DiagnosticSource",
    ];

    private static readonly string[] RoslynExcludedNamePrefixes =
    [
        "Microsoft.CodeAnalysis",
        "Microsoft.Build",
        "Microsoft.EntityFrameworkCore.Design",
        "Humanizer",
        "Mono.TextTemplating",
        "myefvibe",
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

    /// <summary>
    /// Roslyn needs EF Core and Microsoft.Extensions metadata even when those DLLs must not be
    /// scanned from <c>bin/</c> for runtime discovery (version conflicts).
    /// </summary>
    internal static bool ShouldIncludeRoslynMetadata(string dllPath)
    {
        var simpleName = Path.GetFileNameWithoutExtension(dllPath);

        if (string.IsNullOrEmpty(simpleName))
            return false;

        if (ExcludedAssemblyNames.Any(name =>
                string.Equals(simpleName, name, StringComparison.OrdinalIgnoreCase)))
            return false;

        return !RoslynExcludedNamePrefixes.Any(prefix =>
            simpleName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
