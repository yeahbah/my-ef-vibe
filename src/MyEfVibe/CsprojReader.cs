using System.Xml.Linq;

namespace MyEfVibe;

internal static class CsprojReader
{
    internal static string ReadLogicalAssemblyName(string csprojAbsolutePath)
    {
        XDocument dom = XDocument.Load(csprojAbsolutePath);
        XNamespace xmlns = dom.Root!.Name.Namespace;

        foreach (var group in dom.Descendants(xmlns + "PropertyGroup"))
            foreach (var assemblyNameLeaf in group.Elements(xmlns + "AssemblyName"))
            {
                var trimmedAssemblyNameLeaf = assemblyNameLeaf.Value.Trim();

                if (!string.IsNullOrEmpty(trimmedAssemblyNameLeaf))
                    return trimmedAssemblyNameLeaf;
            }

        return Path.GetFileNameWithoutExtension(csprojAbsolutePath);
    }
}
