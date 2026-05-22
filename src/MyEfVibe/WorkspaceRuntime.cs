namespace MyEfVibe;

internal sealed class WorkspaceRuntime : IDisposable
{
    internal WorkspaceRuntime(
        WorkspaceHost host,
        ScriptSession session,
        object dbContext,
        DbLogSettings dbLogSettings,
        string workspaceRoot,
        string sessionDirectory,
        string dbContextName)
    {
        Host = host;
        Session = session;
        DbContext = dbContext;
        DbLogSettings = dbLogSettings;
        WorkspaceRoot = workspaceRoot;
        SessionDirectory = sessionDirectory;
        DbContextName = dbContextName;
    }

    internal WorkspaceHost Host { get; }

    internal ScriptSession Session { get; }

    internal object DbContext { get; }

    internal DbLogSettings DbLogSettings { get; }

    internal string WorkspaceRoot { get; }

    internal string SessionDirectory { get; }

    internal string DbContextName { get; }

    internal SessionAnalytics Analytics { get; } = new();

    public void Dispose() => Host.Dispose();
}
