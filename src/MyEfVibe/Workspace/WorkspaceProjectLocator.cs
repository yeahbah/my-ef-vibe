namespace MyEfVibe.Workspace;

internal static class WorkspaceProjectLocator
{
    internal static FileInfo ResolveProject(string searchDirectory, string? explicitCsprojPathOrNull)
    {
        return WorkspaceProjectSelector.Resolve(searchDirectory, explicitCsprojPathOrNull);
    }
}