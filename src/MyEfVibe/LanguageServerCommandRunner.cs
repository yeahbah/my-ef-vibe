using CommandLine;

namespace MyEfVibe;

internal sealed class LanguageServerCliOptions
{
    [Option('w', "workspace", HelpText = "Session directory for exports and artifacts.")]
    public string? Workspace { get; set; }

    [Option('p', "project", Required = true, HelpText = "EF Core project to build (.csproj with the DbContext).")]
    public string Project { get; set; } = string.Empty;

    [Option('s', "startup-project", HelpText = "Startup project for configuration.")]
    public string? StartupProject { get; set; }

    [Option('c', "context", HelpText = "DbContext type name or fully qualified name.")]
    public string? Context { get; set; }

    [Option("connection-string", HelpText = "Connection string for manual DbContextOptions construction.")]
    public string? ConnectionString { get; set; }

    [Option('f', "framework", HelpText = "Target framework moniker (e.g. net8.0).")]
    public string? Framework { get; set; }
}

internal static class LanguageServerCommandRunner
{
    internal static async Task<int> RunFromOptionsAsync(
        LanguageServerCliOptions options,
        CancellationToken cancellationToken = default)
    {
        var (runtime, exitCode, error) = await WorkspaceRuntimeBootstrap.LoadAsync(
            CliPathHelper.ResolveWorkspace(options.Workspace),
            CliPathHelper.ToFileInfo(options.Project),
            CliPathHelper.ToFileInfo(options.StartupProject),
            options.Context,
            options.ConnectionString,
            false,
            true,
            null,
            false,
            options.Framework,
            cancellationToken);

        if (runtime is null)
        {
            CliUi.WriteError(error ?? "Failed to load workspace for language server.");
            return exitCode;
        }

        using (runtime)
        {
            return await LanguageServerRunner.RunAsync(runtime.DbContext, cancellationToken);
        }
    }
}