using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using MyEfVibe.VisualStudio.Commands;
using MyEfVibe.VisualStudio.Options;
using MyEfVibe.VisualStudio.ToolWindows;
using Task = System.Threading.Tasks.Task;

namespace MyEfVibe.VisualStudio;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("My EF Vibe", "Evaluate EF Core LINQ with efvibe", "0.1.0")]
[Guid(PackageGuids.PackageString)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideOptionPage(typeof(EfvibeOptionsPage), "My EF Vibe", "General", 0, 0, true)]
[ProvideToolWindow(typeof(ResultToolWindow), Style = VsDockStyle.Tabbed, Window = EnvDTE.Constants.vsWindowKindOutput)]
[ProvideToolWindow(typeof(ScanReviewToolWindow), Style = VsDockStyle.Tabbed, Window = EnvDTE.Constants.vsWindowKindOutput)]
[ProvideAutoLoad(Microsoft.VisualStudio.Shell.Interop.UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class MyEfVibePackage : AsyncPackage
{
    internal EfvibeOptionsPage Options =>
        (EfvibeOptionsPage)GetDialogPage(typeof(EfvibeOptionsPage));

    internal async Task<object?> GetServiceInternalAsync(Type serviceType) =>
        await GetServiceAsync(serviceType);

    internal async Task<TWindow> ShowToolWindowAsync<TWindow>(CancellationToken cancellationToken)
        where TWindow : ToolWindowPane
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var window = await FindToolWindowAsync(typeof(TWindow), id: 0, create: true, cancellationToken)
            as TWindow;

        if (window?.Frame is not IVsWindowFrame frame)
            throw new InvalidOperationException($"Could not create tool window {typeof(TWindow).Name}.");

        Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());

        return window;
    }

    protected override async Task InitializeAsync(
        CancellationToken cancellationToken,
        IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        await EfvibeCommands.InitializeAsync(this);
    }
}
