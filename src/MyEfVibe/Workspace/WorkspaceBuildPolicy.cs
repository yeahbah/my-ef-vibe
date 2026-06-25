namespace MyEfVibe.Workspace;

internal enum WorkspaceBuildPolicy
{
    /// <summary>
    ///     Reuse isolated build output when it is newer than project inputs; otherwise run <c>dotnet build</c>.
    /// </summary>
    Auto,

    /// <summary>
    ///     Always run <c>dotnet build</c>, even when cached output is still fresh.
    /// </summary>
    Force,

    /// <summary>
    ///     Never run <c>dotnet build</c>; fail when cached output is missing or stale.
    /// </summary>
    NoBuild
}
