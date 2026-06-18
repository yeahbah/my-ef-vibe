namespace MyEfVibe.Workspace;

internal sealed class WorkspaceException : Exception
{
    public WorkspaceException(string message)
        : base(message)
    {
    }

    public WorkspaceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}