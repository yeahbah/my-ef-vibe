using System;
using System.Text;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using MyEfVibe.VisualStudio.Models;
using MyEfVibe.VisualStudio.Services;
using MyEfVibe.VisualStudio.ToolWindows;

namespace MyEfVibe.VisualStudio.Commands;

internal sealed class EfvibeCommandController
{
    private readonly MyEfVibePackage _package;

    public EfvibeCommandController(MyEfVibePackage package) => _package = package;

    internal async Task StartReplAsync()
    {
        var workspace = await EfvibeWorkspace.ResolveAsync(_package);
        workspace.Runner.StartReplInExternalTerminal(workspace.Settings);
        var window = await _package.ShowToolWindowAsync(_package.DisposalToken);
        window.AppendOutput("REPL", "Started efvibe REPL: " + workspace.Runner.BuildReplCommandLine(workspace.Settings));
    }

    internal async Task RunSelectionAsync(bool withPlan)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

        var expression = await GetEditorExpressionAsync();
        if (string.IsNullOrWhiteSpace(expression))
        {
            ShowWarning("Select a LINQ expression before running efvibe.");
            return;
        }

        var window = await _package.ShowToolWindowAsync(_package.DisposalToken);
        await window.EvaluateExpressionAsync(expression.Trim(), withPlan);
    }

    internal async Task ShowDbInfoAsync()
    {
        var window = await _package.ShowToolWindowAsync(_package.DisposalToken);
        await window.RunDbInfoAsync();
    }

    internal async Task ShowTablesAsync()
    {
        var window = await _package.ShowToolWindowAsync(_package.DisposalToken);
        await window.RunTablesAsync();
    }

    internal async Task ScanAsync(string mode)
    {
        var window = await _package.ShowToolWindowAsync(_package.DisposalToken);
        await window.RunScanAsync(mode);
    }

    internal async Task RefreshConnectionAsync()
    {
        var window = await _package.ShowToolWindowAsync(_package.DisposalToken);
        window.RefreshConnection();
        await _package.UpdateStatusBarAsync(_package.DisposalToken);
    }

    internal async Task CheckPrerequisitesAsync()
    {
        var workspace = await EfvibeWorkspace.ResolveAsync(_package);
        var result = await workspace.Runner.RunAboutJsonAsync(workspace.Settings, _package.DisposalToken);
        var window = await _package.ShowToolWindowAsync(_package.DisposalToken);

        if (result.Succeeded)
        {
            window.AppendOutput("Prerequisites", result.Stdout);
            ShowInfo("efvibe is available.");
        }
        else
        {
            window.AppendOutput("Prerequisites", FormatProcessOutput("efvibe prerequisite check failed", result));
            ShowWarning("efvibe prerequisite check failed. See the My EF Vibe tool window.");
        }
    }

    private async Task<string> GetEditorExpressionAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

        var dte = await _package.GetServiceInternalAsync(typeof(DTE)) as DTE2;
        var selection = dte?.ActiveDocument?.Selection as TextSelection;
        if (selection is null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(selection.Text))
            return selection.Text;

        return selection.ActivePoint.CreateEditPoint().GetLines(selection.CurrentLine, selection.CurrentLine);
    }

    private static string FormatProcessOutput(string title, CliRunResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine(title);
        builder.AppendLine("Exit code: " + result.ExitCode);
        builder.AppendLine();
        builder.AppendLine(result.Stdout);
        builder.AppendLine(result.Stderr);
        return builder.ToString();
    }

    private static void ShowWarning(string message) =>
        VsShellUtilities.ShowMessageBox(
            ServiceProvider.GlobalProvider,
            message,
            "My EF Vibe",
            OLEMSGICON.OLEMSGICON_WARNING,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

    private static void ShowInfo(string message) =>
        VsShellUtilities.ShowMessageBox(
            ServiceProvider.GlobalProvider,
            message,
            "My EF Vibe",
            OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
}
