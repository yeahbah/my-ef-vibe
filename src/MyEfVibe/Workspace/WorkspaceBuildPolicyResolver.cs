namespace MyEfVibe.Workspace;

internal static class WorkspaceBuildPolicyResolver
{
    internal static WorkspaceBuildPolicy Resolve(bool noBuild, bool forceBuild)
    {
        if (noBuild && forceBuild)
        {
            throw new WorkspaceException("Use either --no-build or --force-build, not both.");
        }

        if (noBuild)
        {
            return WorkspaceBuildPolicy.NoBuild;
        }

        if (forceBuild)
        {
            return WorkspaceBuildPolicy.Force;
        }

        return WorkspaceBuildPolicy.Auto;
    }
}
