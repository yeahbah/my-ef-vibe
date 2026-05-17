namespace MyEfVibe;

internal static class WorkspaceProjectLocator
{
    internal static FileInfo ResolveProject(string workspaceDirectory, string? explicitCsprojPathOrNull)
    {
        var normalizedWorkspace =
            Path.GetFullPath(workspaceDirectory.TrimEnd(Path.DirectorySeparatorChar));

        if (!Directory.Exists(normalizedWorkspace))
            throw new WorkspaceException($"Workspace `{normalizedWorkspace}` does not exist.");

        if (!string.IsNullOrWhiteSpace(explicitCsprojPathOrNull))
            return LocateExplicit(explicitCsprojPathOrNull, normalizedWorkspace);

        var shallow = DiscoverCsproj(normalizedWorkspace, recurse: false).ToArray();

        if (shallow.Length == 1)
            return new FileInfo(shallow.Single());

        if (shallow.Length > 1)
            throw new WorkspaceException(
                $"Multiple `.csproj` files were detected under `{normalizedWorkspace}`."
                + $"{Environment.NewLine}Choose one with `-p/--project path/to/File.csproj`.");

        var deep = DiscoverCsproj(normalizedWorkspace, recurse: true)
            .Where(static pathWithoutNoise => ExcludeBinObj(pathWithoutNoise))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        if (deep.Length == 0)
            throw new WorkspaceException($"No `.csproj` files were discovered under `{normalizedWorkspace}`.");

        if (deep.Length > 1)
            throw new WorkspaceException(
                "Multiple `.csproj` files were detected when searching recursively. Specify `-p/--project` with the EF host project.");

        return new FileInfo(deep.Single());
    }

    private static FileInfo LocateExplicit(string explicitCandidate, string normalizedWorkspace)
    {
        var candidatePath =
            Path.IsPathRooted(explicitCandidate)

                ?
                explicitCandidate

                :
                Path.GetFullPath(Path.Combine(normalizedWorkspace, explicitCandidate));

        candidatePath =
            candidatePath.TrimEnd(Path.DirectorySeparatorChar);

        if (!File.Exists(candidatePath))
            throw new WorkspaceException($"Specified project `{candidatePath}` does not exist.");

        if (!string.Equals(Path.GetExtension(candidatePath), ".csproj", StringComparison.OrdinalIgnoreCase))
            throw new WorkspaceException("`-p/--project` must resolve to a `.csproj` file.");

        return new FileInfo(candidatePath);
    }

    private static IEnumerable<string> DiscoverCsproj(string normalizedWorkspaceDirectory, bool recurse)
        =>
        Directory.EnumerateFiles(
            normalizedWorkspaceDirectory,
            "*.csproj",

            recurse
                ?
                SearchOption.AllDirectories


                :

                SearchOption.TopDirectoryOnly);

    private static bool ExcludeBinObj(string absolutePathCandidate)
        =>
        !(absolutePathCandidate.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment =>
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)

                ||

                string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)));
}
