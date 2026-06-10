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

    internal static ProviderDescriptor? TryReadEntityFrameworkProviderDescriptor(string csprojAbsolutePath)
    {
        return EntityFrameworkProviderDiscovery.TryDiscoverFromProject(csprojAbsolutePath);
    }

    internal static MyEfVibeProvider? TryReadEntityFrameworkProvider(string csprojAbsolutePath)
    {
        return TryReadEntityFrameworkProviderDescriptor(csprojAbsolutePath)?.KnownProvider;
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

    internal static IEnumerable<string> EnumeratePackageReferenceIds(string csprojAbsolutePath)
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

    internal static bool TryReadPackageReferenceVersion(
        string csprojAbsolutePath,
        string packageId,
        out Version? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(csprojAbsolutePath) || string.IsNullOrWhiteSpace(packageId))
        {
            return false;
        }

        var dom = Load(csprojAbsolutePath);
        var xmlns = dom.Root!.Name.Namespace;

        foreach (var package in dom.Descendants(xmlns + "PackageReference"))
        {
            var include = package.Attribute("Include")?.Value;

            if (!string.Equals(include, packageId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var versionText = package.Attribute("Version")?.Value?.Trim();

            if (string.IsNullOrWhiteSpace(versionText))
            {
                return false;
            }

            if (Version.TryParse(NormalizeVersionText(versionText), out var parsed))
            {
                version = parsed;
                return true;
            }

            return false;
        }

        return false;
    }

    private static string NormalizeVersionText(string versionText)
    {
        var span = versionText.AsSpan().Trim();

        if (span.Length >= 2
            && (span[0] == '[' || span[0] == '(')
            && span[^1] == ')')
        {
            var inner = span[1..^1];
            var comma = inner.IndexOf(',');

            if (comma >= 0)
            {
                return inner[..comma].Trim().ToString();
            }
        }

        return versionText;
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
