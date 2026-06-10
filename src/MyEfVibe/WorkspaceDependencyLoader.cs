using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace MyEfVibe;

internal static class WorkspaceDependencyLoader
{
    internal static void PreloadCorePackages(
        AssemblyLoadContext loadContext,
        WorkspaceDepsManifest depsManifest)
    {
        PreloadRequiredAssembly(loadContext, depsManifest, "System.Configuration.ConfigurationManager");

        PreloadEfReferenceAssemblies(loadContext, depsManifest);

        foreach (var bootstrap in new[]
                 {
                     "Microsoft.Data.SqlClient",
                     "Microsoft.Extensions.Caching.Abstractions",
                     "Microsoft.Extensions.Caching.Memory",
                     "Microsoft.Extensions.DependencyInjection",
                     "Microsoft.Extensions.DependencyInjection.Abstractions",
                     "Microsoft.Extensions.Logging",
                     "Microsoft.Extensions.Logging.Abstractions",
                     "Microsoft.Extensions.Options",
                     "Microsoft.EntityFrameworkCore.Abstractions",
                     "Microsoft.EntityFrameworkCore",
                     "Microsoft.EntityFrameworkCore.Relational",
                     "Microsoft.EntityFrameworkCore.SqlServer",
                     "Npgsql.EntityFrameworkCore.PostgreSQL",
                     "EFCore.NamingConventions",
                     "Microsoft.EntityFrameworkCore.Sqlite",
                     "Oracle.EntityFrameworkCore",
                     "Pomelo.EntityFrameworkCore.MySql",
                     "MySql.EntityFrameworkCore",
                     "Couchbase.EntityFrameworkCore",
                     "Couchbase.NetClient",
                     "Microsoft.Extensions.ObjectPool"
                 })
        {
            if (!depsManifest.TryResolve(bootstrap, false, out var bootstrapPath))
            {
                depsManifest.TryResolve(bootstrap, true, out bootstrapPath);
            }

            if (!string.IsNullOrEmpty(bootstrapPath))
            {
                TryLoad(loadContext, bootstrapPath);
            }
        }
    }

    internal static void PreloadStartupReferenceClosure(
        AssemblyLoadContext loadContext,
        WorkspaceDepsManifest depsManifest,
        string startupAssemblyPath,
        string entryAssemblyPath)
    {
        if (!File.Exists(startupAssemblyPath)
            || string.Equals(startupAssemblyPath, entryAssemblyPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var entrySimpleName = Path.GetFileNameWithoutExtension(entryAssemblyPath);

        PreloadAssemblyReferenceClosure(
            loadContext,
            depsManifest,
            startupAssemblyPath,
            excludedSimpleName: entrySimpleName);
    }

    internal static void Preload(
        AssemblyLoadContext loadContext,
        AssemblyDependencyResolver resolver,
        string entryAssemblyPath,
        WorkspaceDepsManifest? depsManifest,
        string? additionalReferenceClosureAssemblyPath = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (depsManifest is not null)
        {
            PreloadCorePackages(loadContext, depsManifest);

            if (!string.IsNullOrWhiteSpace(additionalReferenceClosureAssemblyPath))
            {
                PreloadStartupReferenceClosure(
                    loadContext,
                    depsManifest,
                    additionalReferenceClosureAssemblyPath,
                    entryAssemblyPath);
            }

            return;
        }

        PreloadUsingDependencyResolverOnly(loadContext, resolver, entryAssemblyPath, seen);
    }

    private static void PreloadUsingDependencyResolverOnly(
        AssemblyLoadContext loadContext,
        AssemblyDependencyResolver resolver,
        string entryAssemblyPath,
        HashSet<string> seen)
    {
        var outputDirectory = Path.GetDirectoryName(entryAssemblyPath)!;

        var depsPath = Path.Combine(
            outputDirectory,
            $"{Path.GetFileNameWithoutExtension(entryAssemblyPath)}.deps.json");

        if (!File.Exists(depsPath))
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(depsPath));

        if (!document.RootElement.TryGetProperty("runtimeTarget", out var runtimeTargetProperty))
        {
            return;
        }

        if (!document.RootElement.TryGetProperty("targets", out var targets))
        {
            return;
        }

        if (!runtimeTargetProperty.TryGetProperty("name", out var runtimeTargetNameProperty))
        {
            return;
        }

        var runtimeTargetName = runtimeTargetNameProperty.GetString();

        if (string.IsNullOrWhiteSpace(runtimeTargetName)
            || !targets.TryGetProperty(runtimeTargetName, out var targetNode))
        {
            return;
        }

        foreach (var assemblySimpleName in EnumerateRuntimeAssemblySimpleNames(targetNode))
        {
            if (!seen.Add(assemblySimpleName))
            {
                continue;
            }

            if (IsDesignTimeOrToolingAssembly(assemblySimpleName))
            {
                continue;
            }

            var resolvedPath = resolver.ResolveAssemblyToPath(new AssemblyName(assemblySimpleName));

            if (resolvedPath is null)
            {
                continue;
            }

            TryLoad(loadContext, resolvedPath);
        }
    }

    private static IEnumerable<string> EnumerateRuntimeAssemblySimpleNames(JsonElement targetNode)
    {
        foreach (var library in targetNode.EnumerateObject())
        {
            if (!library.Value.TryGetProperty("runtime", out var runtimeAssets))
            {
                continue;
            }

            foreach (var runtimeAsset in runtimeAssets.EnumerateObject())
            {
                var fileName = Path.GetFileName(runtimeAsset.Name);

                if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return Path.GetFileNameWithoutExtension(fileName);
            }
        }
    }

    private static void PreloadRequiredAssembly(
        AssemblyLoadContext loadContext,
        WorkspaceDepsManifest depsManifest,
        string assemblySimpleName)
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                string.Equals(assembly.GetName().Name, assemblySimpleName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        foreach (var path in depsManifest.EnumerateConfigurationManagerHostCandidates())
        {
            try
            {
                AssemblyResolutionHelpers.LoadFromPath(loadContext, path);

                return;
            }
            catch (BadImageFormatException)
            {
            }
            catch (FileLoadException)
            {
            }
        }
    }

    private static void PreloadEfReferenceAssemblies(
        AssemblyLoadContext loadContext,
        WorkspaceDepsManifest depsManifest)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rootAssembly in new[]
                 {
                     "Microsoft.EntityFrameworkCore",
                     "Microsoft.EntityFrameworkCore.Relational",
                     "Microsoft.EntityFrameworkCore.SqlServer",
                     "Npgsql.EntityFrameworkCore.PostgreSQL",
                     "EFCore.NamingConventions",
                     "Microsoft.EntityFrameworkCore.Sqlite",
                     "Oracle.EntityFrameworkCore",
                     "Pomelo.EntityFrameworkCore.MySql",
                     "MySql.EntityFrameworkCore",
                     "Couchbase.EntityFrameworkCore",
                     "Couchbase.NetClient"
                 })
        {
            if (depsManifest.TryResolve(rootAssembly, false, out var rootPath))
            {
                PreloadAssemblyReferenceClosure(
                    loadContext,
                    depsManifest,
                    rootPath,
                    visited,
                    visiting);
            }
        }
    }

    /// <summary>
    ///     Loads an assembly and its reference closure in dependency-first order so transitive
    ///     packages (e.g. Microsoft.Extensions.Caching.Abstractions) are available with correct versions.
    /// </summary>
    internal static void PreloadAssemblyReferenceClosure(
        AssemblyLoadContext loadContext,
        WorkspaceDepsManifest depsManifest,
        string rootAssemblyPath,
        HashSet<string>? sharedVisited = null,
        HashSet<string>? sharedVisiting = null,
        string? excludedSimpleName = null)
    {
        var visiting = sharedVisiting ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = sharedVisited ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Visit(rootAssemblyPath);

        void Visit(string assemblyPath)
        {
            if (!string.IsNullOrEmpty(excludedSimpleName)
                && string.Equals(
                    Path.GetFileNameWithoutExtension(assemblyPath),
                    excludedSimpleName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (visited.Contains(assemblyPath))
            {
                return;
            }

            if (!visiting.Add(assemblyPath))
            {
                return;
            }

            foreach (var reference in AssemblyReferenceReader.Read(assemblyPath))
            {
                if (string.IsNullOrEmpty(reference.Name))
                {
                    continue;
                }

                if (!depsManifest.TryResolve(reference, false, out var referencePath))
                {
                    depsManifest.TryResolve(reference, true, out referencePath);
                }

                if (!string.IsNullOrEmpty(referencePath))
                {
                    Visit(referencePath);
                }
            }

            visiting.Remove(assemblyPath);
            visited.Add(assemblyPath);
            TryLoad(loadContext, assemblyPath);
            depsManifest.RegisterProviderAssemblyReferencesFromPath(assemblyPath);
        }
    }

    private static bool IsDesignTimeOrToolingAssembly(string assemblySimpleName)
    {
        return assemblySimpleName.StartsWith("Microsoft.Build", StringComparison.OrdinalIgnoreCase)
               || assemblySimpleName.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)
               || assemblySimpleName.StartsWith("Microsoft.EntityFrameworkCore.Design",
                   StringComparison.OrdinalIgnoreCase)
               || assemblySimpleName.StartsWith("Humanizer", StringComparison.OrdinalIgnoreCase)
               || assemblySimpleName.StartsWith("Mono.TextTemplating", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryLoad(AssemblyLoadContext loadContext, string absolutePath)
    {
        try
        {
            AssemblyResolutionHelpers.LoadFromPath(loadContext, absolutePath);
        }
        catch (BadImageFormatException)
        {
        }
        catch (FileLoadException)
        {
        }
    }
}