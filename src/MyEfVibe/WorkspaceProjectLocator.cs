namespace MyEfVibe;

internal static class WorkspaceProjectLocator
{
    internal static FileInfo ResolveProject(string searchDirectory, string? explicitCsprojPathOrNull)
        => WorkspaceProjectSelector.Resolve(searchDirectory, explicitCsprojPathOrNull);
}
