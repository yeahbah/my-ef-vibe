using System.Text.RegularExpressions;

namespace MyEfVibe;

internal static partial class DbContextSourceScanner
{
    [GeneratedRegex(@"class\s+\w+\s*:\s*DbContext\b", RegexOptions.CultureInvariant)]
    private static partial Regex DbContextClassDeclarationPattern();

    internal static bool ProjectContainsDbContext(string projectDirectory)
    {
        if (!Directory.Exists(projectDirectory))
        {
            return false;
        }

        foreach (var sourcePath in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (IsUnderBuildArtifacts(sourcePath))
            {
                continue;
            }

            try
            {
                if (DbContextClassDeclarationPattern().IsMatch(File.ReadAllText(sourcePath)))
                {
                    return true;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return false;
    }

    private static bool IsUnderBuildArtifacts(string absolutePath)
    {
        foreach (var segment in absolutePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}