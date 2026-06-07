using System.Xml.Linq;

namespace MyEfVibe;

internal static class CsprojInspector
{
    private static readonly string[] EfCorePackagePrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql.EntityFrameworkCore",
        "Pomelo.EntityFrameworkCore",
        "MySql.EntityFrameworkCore",
        "MariaDB.EntityFrameworkCore",
        "Oracle.EntityFrameworkCore"
    ];

    private static readonly (string PackageId, MyEfVibeProvider Provider)[] KnownEntityFrameworkProviderPackages =
    [
        ("Microsoft.EntityFrameworkCore.SqlServer", MyEfVibeProvider.SqlServer),
        ("Npgsql.EntityFrameworkCore.PostgreSQL", MyEfVibeProvider.Npgsql),
        ("Microsoft.EntityFrameworkCore.Sqlite", MyEfVibeProvider.Sqlite),
        ("Oracle.EntityFrameworkCore", MyEfVibeProvider.Oracle),
        ("MariaDB.EntityFrameworkCore", MyEfVibeProvider.MariaDb),
        ("Pomelo.EntityFrameworkCore.MySql", MyEfVibeProvider.MySql),
        ("MySql.EntityFrameworkCore", MyEfVibeProvider.MySql)
    ];

    internal static bool HasEfCorePackageReference(string csprojAbsolutePath)
    {
        foreach (var packageId in EnumeratePackageReferenceIds(csprojAbsolutePath))
        {
            if (EfCorePackagePrefixes.Any(prefix =>
                    packageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    internal static MyEfVibeProvider? TryReadEntityFrameworkProvider(string csprojAbsolutePath)
    {
        if (string.IsNullOrWhiteSpace(csprojAbsolutePath))
        {
            return null;
        }

        var providers = new HashSet<MyEfVibeProvider>();
        CollectEntityFrameworkProviders(
            Path.GetFullPath(csprojAbsolutePath),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            providers);

        return providers.Count == 1 ? providers.First() : null;
    }

    internal static bool UsesWebSdk(string csprojAbsolutePath)
    {
        var dom = Load(csprojAbsolutePath);

        return dom.Root?.Name.LocalName.Equals("Project", StringComparison.Ordinal) == true
               && dom.Root.Attribute("Sdk")?.Value.Contains("Web", StringComparison.OrdinalIgnoreCase) == true;
    }

    internal static bool IsExecutableOutput(string csprojAbsolutePath)
    {
        var outputType = ReadProperty(csprojAbsolutePath, "OutputType");

        return string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase)
               || string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase)
               || UsesWebSdk(csprojAbsolutePath);
    }

    internal static bool TryGetUserSecretsId(string csprojAbsolutePath, out string userSecretsId)
    {
        userSecretsId = ReadProperty(csprojAbsolutePath, "UserSecretsId") ?? string.Empty;

        return !string.IsNullOrWhiteSpace(userSecretsId);
    }

    internal static bool IsTestProject(string csprojAbsolutePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(csprojAbsolutePath);
        var fullPath = csprojAbsolutePath;

        if (fileName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".Test", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(".Test.", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".UnitTests", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (ReadProperty(csprojAbsolutePath, "IsTestProject")
                ?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(static segment =>
                string.Equals(segment, "tests", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "test", StringComparison.OrdinalIgnoreCase));
    }

    internal static IReadOnlyList<string> GetProjectReferencePaths(string csprojAbsolutePath)
    {
        var projectDirectory = Path.GetDirectoryName(csprojAbsolutePath)!;
        var dom = Load(csprojAbsolutePath);
        var xmlns = dom.Root!.Name.Namespace;
        var references = new List<string>();

        foreach (var reference in dom.Descendants(xmlns + "ProjectReference"))
        {
            var include = reference.Attribute("Include")?.Value;

            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            var normalized = Path.GetFullPath(Path.Combine(projectDirectory, NormalizeProjectReferencePath(include)));

            if (File.Exists(normalized))
            {
                references.Add(normalized);
            }
        }

        return references;
    }

    internal static string NormalizeProjectReferencePath(string include)
    {
        return include.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static void CollectEntityFrameworkProviders(
        string csprojAbsolutePath,
        HashSet<string> visitedProjects,
        HashSet<MyEfVibeProvider> providers)
    {
        if (!visitedProjects.Add(csprojAbsolutePath) || !File.Exists(csprojAbsolutePath))
        {
            return;
        }

        foreach (var packageId in EnumeratePackageReferenceIds(csprojAbsolutePath))
        {
            var provider = MapEntityFrameworkProviderPackage(packageId);

            if (provider.HasValue)
            {
                providers.Add(provider.Value);
            }
        }

        foreach (var reference in GetProjectReferencePaths(csprojAbsolutePath))
        {
            CollectEntityFrameworkProviders(reference, visitedProjects, providers);
        }
    }

    private static MyEfVibeProvider? MapEntityFrameworkProviderPackage(string packageId)
    {
        foreach (var (knownPackageId, provider) in KnownEntityFrameworkProviderPackages)
        {
            if (string.Equals(packageId, knownPackageId, StringComparison.OrdinalIgnoreCase))
            {
                return provider;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumeratePackageReferenceIds(string csprojAbsolutePath)
    {
        var dom = Load(csprojAbsolutePath);
        var xmlns = dom.Root!.Name.Namespace;

        foreach (var package in dom.Descendants(xmlns + "PackageReference"))
        {
            var include = package.Attribute("Include")?.Value;

            if (!string.IsNullOrWhiteSpace(include))
            {
                yield return include;
            }
        }
    }

    private static string? ReadProperty(string csprojAbsolutePath, string propertyName)
    {
        var dom = Load(csprojAbsolutePath);
        var xmlns = dom.Root!.Name.Namespace;

        foreach (var group in dom.Descendants(xmlns + "PropertyGroup"))
        foreach (var property in group.Elements(xmlns + propertyName))
        {
            var value = property.Value.Trim();

            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return null;
    }

    private static XDocument Load(string csprojAbsolutePath)
    {
        return XDocument.Load(csprojAbsolutePath);
    }
}