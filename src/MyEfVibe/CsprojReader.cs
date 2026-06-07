using System.Xml.Linq;

namespace MyEfVibe;

internal static class CsprojReader
{
    internal static string ReadLogicalAssemblyName(string csprojAbsolutePath)
    {
        var dom = XDocument.Load(csprojAbsolutePath);
        var xmlns = dom.Root!.Name.Namespace;

        foreach (var group in dom.Descendants(xmlns + "PropertyGroup"))
        {
            foreach (var assemblyNameLeaf in group.Elements(xmlns + "AssemblyName"))
            {
                var trimmedAssemblyNameLeaf = assemblyNameLeaf.Value.Trim();

                if (!string.IsNullOrEmpty(trimmedAssemblyNameLeaf))
                {
                    return trimmedAssemblyNameLeaf;
                }
            }    
        }
        

        return Path.GetFileNameWithoutExtension(csprojAbsolutePath);
    }

    internal static string[] ReadTargetFrameworkMonikers(string csprojAbsolutePath)
    {
        var dom = XDocument.Load(csprojAbsolutePath);
        var xmlns = dom.Root!.Name.Namespace;
        var monikers = new List<string>();

        foreach (var group in dom.Descendants(xmlns + "PropertyGroup"))
        {
            foreach (var leaf in group.Elements(xmlns + "TargetFramework"))
            {
                var value = leaf.Value.Trim();

                if (!string.IsNullOrEmpty(value))
                {
                    monikers.Add(ProjectTargetFrameworkResolver.NormalizeMoniker(value));
                }
            }

            foreach (var leaf in group.Elements(xmlns + "TargetFrameworks"))
            {
                var entries = leaf.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var value in entries)
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        monikers.Add(ProjectTargetFrameworkResolver.NormalizeMoniker(value));
                    }
                }
            }
        }

        return monikers
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}