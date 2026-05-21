using System.Text.RegularExpressions;

namespace MyEfVibe;

internal static partial class DbContextClassDiscovery
{
    [GeneratedRegex(
        @"class\s+(?<Name>\w+)\s*:\s*(?:\w+(?:<[^>]+>)?\s*,\s*)*DbContext\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex DbContextClassDeclarationRegex();

    internal static IReadOnlyList<string> DiscoverDbContextTypeNames(IEnumerable<string> projectDirectories)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var projectDirectory in projectDirectories)
        {
            if (!Directory.Exists(projectDirectory))
                continue;

            foreach (var sourcePath in LinqProjectSourceWalker.EnumerateSourceFiles(projectDirectory))
            {
                try
                {
                    var text = File.ReadAllText(sourcePath);

                    foreach (Match match in DbContextClassDeclarationRegex().Matches(text))
                    {
                        if (match.Groups["Name"].Success)
                            names.Add(match.Groups["Name"].Value);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        return names.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
    }
}
