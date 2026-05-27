using System;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using MyEfVibe.VisualStudio.Options;

namespace MyEfVibe.VisualStudio.Services;

internal static class EfvibeWorkspace
{
    internal static async Task<WorkspaceContext> ResolveAsync(
        MyEfVibePackage package,
        CancellationToken cancellationToken = default)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var dte = await package.GetServiceInternalAsync(typeof(DTE)) as DTE2;
        var solution = SolutionContext.FromDte(dte);
        var settings = EfvibeSettings.FromOptions(package.Options, solution.SolutionDirectory);

        if (string.IsNullOrWhiteSpace(settings.Project))
        {
            throw new InvalidOperationException(
                "Set My EF Vibe > General > EF project before running efvibe.");
        }

        var runner = new CliRunner(solution.SolutionDirectory);
        var session = package.GetSessionState(solution.SolutionDirectory);

        return new WorkspaceContext(settings, runner, session, solution.SolutionDirectory);
    }

    internal sealed class WorkspaceContext
    {
        public WorkspaceContext(
            EfvibeSettings settings,
            CliRunner runner,
            EfvibeSessionState session,
            string solutionDirectory)
        {
            Settings = settings;
            Runner = runner;
            Session = session;
            SolutionDirectory = solutionDirectory;
        }

        public EfvibeSettings Settings { get; }
        public CliRunner Runner { get; }
        public EfvibeSessionState Session { get; }
        public string SolutionDirectory { get; }
    }
}
