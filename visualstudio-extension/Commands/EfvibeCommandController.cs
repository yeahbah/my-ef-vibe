using System;
using System.Linq;
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

    public EfvibeCommandController(MyEfVibePackage package)
    {
        _package = package;
    }

    internal async Task StartReplAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

        var context = await CreateContextAsync();
        context.Runner.StartReplInExternalTerminal(context.Settings);
        await WriteOutputAsync("Started efvibe REPL: " + context.Runner.BuildReplCommandLine(context.Settings));
    }

    internal async Task RunSelectionAsync(bool withPlan)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

        var expression = await GetSelectedTextAsync();

        if (string.IsNullOrWhiteSpace(expression))
        {
            ShowWarning("Select a LINQ expression before running efvibe.");
            return;
        }

        await RunExpressionAsync(expression.Trim(), withPlan);
    }

    internal async Task ShowDbInfoAsync()
    {
        var context = await CreateContextAsync();
        var result = await context.Runner.RunDbInfoJsonAsync(context.Settings, _package.DisposalToken);
        var payload = JsonLineParser.ParseFirstJsonLine<DbInfoJsonPayload>(result.Stdout);
        var text = payload is null ? FormatProcessOutput("dbinfo failed", result) : FormatDbInfo(payload);
        var window = await _package.ShowToolWindowAsync<ResultToolWindow>(_package.DisposalToken);
        window.ShowText(":dbinfo", text);
        await WriteOutputAsync(text);
    }

    internal async Task ShowTablesAsync()
    {
        var context = await CreateContextAsync();
        var result = await context.Runner.RunTablesJsonAsync(context.Settings, _package.DisposalToken);
        var payload = JsonLineParser.ParseFirstJsonLine<TablesJsonPayload>(result.Stdout);
        var text = payload is null ? FormatProcessOutput("tables failed", result) : FormatTables(payload);
        var window = await _package.ShowToolWindowAsync<ResultToolWindow>(_package.DisposalToken);
        window.ShowText(":tables", text);
        await WriteOutputAsync(text);
    }

    internal async Task DescribeEntityAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

        var entityName = InputDialog.Prompt("Describe entity", "DbSet or entity type name:");

        if (string.IsNullOrWhiteSpace(entityName))
            return;

        var context = await CreateContextAsync();
        var requestedEntity = entityName!.Trim();
        var result = await context.Runner.RunDescribeJsonAsync(context.Settings, requestedEntity, _package.DisposalToken);
        var payload = JsonLineParser.ParseFirstJsonLine<DescribeJsonPayload>(result.Stdout);
        var text = payload is null ? FormatProcessOutput("describe failed", result) : FormatDescribe(payload);
        var window = await _package.ShowToolWindowAsync<ResultToolWindow>(_package.DisposalToken);
        window.ShowText(":describe " + requestedEntity, text);
        await WriteOutputAsync(text);
    }

    internal async Task ScanAsync(string mode)
    {
        var context = await CreateContextAsync();
        await WriteOutputAsync($"Running efvibe scan {mode}...");
        var result = await context.Runner.RunScanAsync(context.Settings, mode, _package.DisposalToken);
        var payload = JsonLineParser.ParseFirstJsonLine<ScanCiOutputDocument>(result.Stdout);

        if (payload is null)
        {
            await WriteOutputAsync(FormatProcessOutput($"scan {mode} failed", result));
            ShowWarning($"efvibe scan {mode} did not return JSON. See Output > My EF Vibe.");
            return;
        }

        var window = await _package.ShowToolWindowAsync<ScanReviewToolWindow>(_package.DisposalToken);
        window.ShowScan(payload);
        await WriteOutputAsync($"scan {payload.ScanMode}: {payload.TotalFindings} finding(s), saved to {payload.SavedPath}");
    }

    internal async Task CheckPrerequisitesAsync()
    {
        var context = await CreateContextAsync();
        var result = await context.Runner.RunAboutJsonAsync(context.Settings, _package.DisposalToken);

        if (result.Succeeded)
        {
            await WriteOutputAsync("efvibe is available." + Environment.NewLine + result.Stdout);
            ShowInfo("efvibe is available.");
            return;
        }

        await WriteOutputAsync(FormatProcessOutput("efvibe prerequisite check failed", result));
        ShowWarning("efvibe prerequisite check failed. See Output > My EF Vibe.");
    }

    private async Task RunExpressionAsync(string expression, bool withPlan)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

        if (!ExpressionGuard.IsReadOnly(expression, out var reason))
        {
            ShowWarning(reason);
            return;
        }

        var window = await _package.ShowToolWindowAsync<ResultToolWindow>(_package.DisposalToken);
        window.SetRunner(RunExpressionAsync);
        window.ShowText(expression, "Running efvibe...");

        var context = await CreateContextAsync();
        var result = await context.Runner.RunExpressionJsonAsync(context.Settings, expression, withPlan, _package.DisposalToken);
        var payload = JsonLineParser.ParseFirstJsonLine<EvaluationJsonPayload>(result.Stdout);

        if (payload is null)
        {
            window.ShowText(expression, FormatProcessOutput("evaluation failed", result));
            ShowWarning("efvibe did not return JSON. See the result window for details.");
            return;
        }

        window.ShowEvaluation(expression, payload);

        if (!payload.Success)
            ShowWarning(payload.Error ?? "efvibe evaluation failed.");
    }

    private async Task<CommandContext> CreateContextAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

        var dte = await _package.GetServiceInternalAsync(typeof(SDTE)) as DTE2;
        var solution = Services.SolutionContext.FromDte(dte);
        var settings = EfvibeSettings.FromOptions(_package.Options, solution.SolutionDirectory);

        if (string.IsNullOrWhiteSpace(settings.Project))
            throw new InvalidOperationException("Set My EF Vibe > General > EF project before running efvibe.");

        return new CommandContext(settings, new CliRunner(solution.SolutionDirectory));
    }

    private async Task<string> GetSelectedTextAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

        var dte = await _package.GetServiceInternalAsync(typeof(SDTE)) as DTE2;
        var selection = dte?.ActiveDocument?.Selection as TextSelection;

        return selection?.Text ?? string.Empty;
    }

    private async Task WriteOutputAsync(string text)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_package.DisposalToken);

        if (await _package.GetServiceInternalAsync(typeof(SVsOutputWindow)) is not IVsOutputWindow outputWindow)
            return;

        var paneGuid = new Guid(PackageGuids.OutputPaneString);
        ErrorHandler.ThrowOnFailure(outputWindow.CreatePane(ref paneGuid, "My EF Vibe", 1, 1));
        ErrorHandler.ThrowOnFailure(outputWindow.GetPane(ref paneGuid, out var pane));
        pane?.OutputStringThreadSafe(text + Environment.NewLine);
    }

    private static string FormatDbInfo(DbInfoJsonPayload payload)
    {
        var builder = new StringBuilder();
        builder.AppendLine("DbContext: " + payload.DbContext);

        foreach (var entry in payload.Entries ?? Enumerable.Empty<DbInfoJsonEntry>())
            builder.AppendLine($"{entry.Key}: {entry.Value}");

        return builder.ToString();
    }

    private static string FormatTables(TablesJsonPayload payload)
    {
        var builder = new StringBuilder();
        builder.AppendLine("DbContext: " + payload.DbContext);

        foreach (var table in payload.Tables ?? Enumerable.Empty<TablesJsonEntry>())
            builder.AppendLine($"{table.DbSet} -> {table.EntityType}");

        return builder.ToString();
    }

    private static string FormatDescribe(DescribeJsonPayload payload)
    {
        if (!payload.Success)
            return payload.Error ?? "Describe failed.";

        var builder = new StringBuilder();
        builder.AppendLine($"{payload.DbSet ?? payload.EntityType}: {payload.EntityTypeFullName}");

        foreach (var member in payload.Members ?? Enumerable.Empty<DescribeJsonMember>())
            builder.AppendLine($"{member.Name}: {member.Type} nullable={member.Nullable} {member.Notes}");

        return builder.ToString();
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

    private sealed class CommandContext
    {
        public CommandContext(EfvibeSettings settings, CliRunner runner)
        {
            Settings = settings;
            Runner = runner;
        }

        public EfvibeSettings Settings { get; }
        public CliRunner Runner { get; }
    }
}
