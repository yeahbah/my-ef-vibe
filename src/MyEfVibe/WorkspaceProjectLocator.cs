namespace MyEfVibe;

internal static class WorkspaceProjectLocator
{
    internal static FileInfo ResolveProject(string workspaceDirectory, string? explicitCsprojPathOrNull)
        => WorkspaceProjectSelector.Resolve(workspaceDirectory, explicitCsprojPathOrNull);
}
