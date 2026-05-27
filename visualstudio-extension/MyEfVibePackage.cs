using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using MyEfVibe.VisualStudio.Commands;
using MyEfVibe.VisualStudio.Options;
using MyEfVibe.VisualStudio.Services;
using MyEfVibe.VisualStudio.ToolWindows;
using Task = System.Threading.Tasks.Task;

namespace MyEfVibe.VisualStudio;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("My EF Vibe", "Evaluate EF Core LINQ with efvibe", "0.1.0")]
[Guid(PackageGuids.PackageString)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideOptionPage(typeof(EfvibeOptionsPage), "My EF Vibe", "General", 0, 0, true)]
[ProvideToolWindow(typeof(ResultToolWindow), Style = VsDockStyle.Tabbed, Window = EnvDTE.Constants.vsWindowKindOutput)]
[ProvideAutoLoad(Microsoft.VisualStudio.Shell.Interop.UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class MyEfVibePackage : AsyncPackage
{
    private static readonly ConcurrentDictionary<string, EfvibeSessionState> SessionStates = new(StringComparer.OrdinalIgnoreCase);

    internal static MyEfVibePackage? Instance { get; private set; }

    internal EfvibeOptionsPage Options =>
        (EfvibeOptionsPage)GetDialogPage(typeof(EfvibeOptionsPage));

    internal EfvibeSessionState GetSessionState(string solutionDirectory) =>
        SessionStates.GetOrAdd(solutionDirectory, _ => new EfvibeSessionState());

    internal async Task<object?> GetServiceInternalAsync(Type serviceType) =>
        await GetServiceAsync(serviceType);

    internal async Task<ResultToolWindow> ShowToolWindowAsync(CancellationToken cancellationToken)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var window = await FindToolWindowAsync(typeof(ResultToolWindow), id: 0, create: true, cancellationToken)
            as ResultToolWindow;

        if (window?.Frame is not IVsWindowFrame frame)
            throw new InvalidOperationException("Could not create the My EF Vibe tool window.");

        Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());
        return window;
    }

    protected override async Task InitializeAsync(
        CancellationToken cancellationToken,
        IProgress<ServiceProgressData> progress)
    {
        Instance = this;
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        await EfvibeCommands.InitializeAsync(this);
        await UpdateStatusBarAsync(cancellationToken);
    }

    internal async Task UpdateStatusBarAsync(CancellationToken cancellationToken)
    {
        try
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            if (await GetServiceAsync(typeof(SVsStatusbar)) is not IVsStatusbar statusBar)
                return;

            var workspace = await EfvibeWorkspace.ResolveAsync(this, cancellationToken);
            var context = workspace.Settings.Context.IfBlank("efvibe");
            statusBar.SetText($"My EF Vibe: {context}");
        }
        catch
        {
            if (await GetServiceAsync(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
                statusBar.SetText("My EF Vibe");
        }
    }
}
