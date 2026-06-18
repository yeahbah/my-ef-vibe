using System.Reflection;

namespace MyEfVibe.Workspace;

internal sealed class WorkspaceProviderAssemblyRegistry
{
    private readonly HashSet<string> _nuGetFallbackAssemblyNames =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _assemblyNuGetPackageFolders =
        new(StringComparer.OrdinalIgnoreCase);

    internal void Register(ProviderDescriptor descriptor)
    {
        foreach (var assemblySimpleName in ProviderAssemblyNames.For(descriptor))
        {
            var packageFolder = string.Equals(
                    assemblySimpleName,
                    descriptor.ProviderAssemblyName,
                    StringComparison.OrdinalIgnoreCase)
                ? GetPrimaryAssemblyPackageFolder(descriptor)
                : ProviderAssemblyNames.GetNuGetPackageFolderName(assemblySimpleName);

            RegisterAssembly(assemblySimpleName, packageFolder);
        }
    }

    internal void RegisterAssemblyReferences(Assembly assembly)
    {
        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            if (string.IsNullOrWhiteSpace(reference.Name))
            {
                continue;
            }

            RegisterAssembly(reference.Name, ProviderAssemblyNames.GetNuGetPackageFolderName(reference.Name));
        }
    }

    internal bool AllowsNuGetFallback(string? assemblySimpleName)
    {
        return !string.IsNullOrWhiteSpace(assemblySimpleName)
               && _nuGetFallbackAssemblyNames.Contains(assemblySimpleName);
    }

    internal string GetNuGetPackageFolderName(string assemblySimpleName)
    {
        return _assemblyNuGetPackageFolders.TryGetValue(assemblySimpleName, out var packageFolder)
            ? packageFolder
            : ProviderAssemblyNames.GetNuGetPackageFolderName(assemblySimpleName);
    }

    private static string GetPrimaryAssemblyPackageFolder(ProviderDescriptor descriptor)
    {
        if (descriptor.KnownProvider.HasValue)
        {
            return ProviderAssemblyNames.GetNuGetPackageFolderName(descriptor.ProviderAssemblyName);
        }

        return EntityFrameworkProviderCatalog.GetNuGetPackageFolderName(descriptor.PackageId);
    }

    private void RegisterAssembly(string assemblySimpleName, string nuGetPackageFolder)
    {
        _nuGetFallbackAssemblyNames.Add(assemblySimpleName);
        _assemblyNuGetPackageFolders[assemblySimpleName] = nuGetPackageFolder;
    }
}
