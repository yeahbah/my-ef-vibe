using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyEfVibe.VisualStudio.Models;

namespace MyEfVibe.VisualStudio.Services;

internal sealed class CliRunner
{
    private readonly string _solutionDirectory;

    public CliRunner(string solutionDirectory)
    {
        _solutionDirectory = solutionDirectory;
    }

    public CliInvocation ResolveInvocation(EfvibeSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ToolPath))
        {
            return new CliInvocation { Command = settings.ToolPath };
        }

        if (FindDotnetToolsManifest(_solutionDirectory) is not null)
        {
            var invocation = new CliInvocation { Command = "dotnet" };
            invocation.PrefixArgs.Add("efvibe");
            return invocation;
        }

        return new CliInvocation { Command = "efvibe" };
    }

    public IReadOnlyList<string> BuildBaseArgs(EfvibeSettings settings)
    {
        var args = new List<string>();

        AddOption(args, "-w", settings.WorkspaceRoot);
        AddOption(args, "-p", settings.Project);
        AddOption(args, "-s", settings.StartupProject);
        AddOption(args, "-c", settings.Context);
        AddOption(args, "--connection-string", settings.ConnectionString);
        AddOption(args, "--provider", settings.Provider);

        if (!settings.DbLog)
            args.Add("--no-dblog");

        AddOption(args, "--framework", settings.DotnetFramework);

        return args;
    }

    public string BuildReplCommandLine(EfvibeSettings settings)
    {
        var invocation = ResolveInvocation(settings);
        var args = new List<string>(invocation.PrefixArgs);
        args.AddRange(BuildBaseArgs(settings));

        return BuildCommandLine(invocation.Command, args);
    }

    public CliInvocationSpec BuildServeSpec(EfvibeSettings settings)
    {
        var invocation = ResolveInvocation(settings);
        var args = new List<string>(invocation.PrefixArgs);
        args.Add("serve");
        args.AddRange(BuildBaseArgs(settings));

        return new CliInvocationSpec
        {
            Command = invocation.Command,
            Args = args.ToArray(),
            WorkingDirectory = _solutionDirectory,
        };
    }

    public async Task<ExpressionRunResult> RunExpressionPayloadAsync(
        EfvibeWorkspace.WorkspaceContext workspace,
        string expression,
        bool withPlan,
        bool preferDaemon,
        CancellationToken cancellationToken)
    {
        var settings = workspace.Settings;

        if (preferDaemon)
        {
            try
            {
                var daemon = EfvibeDaemonClient.GetOrCreate(workspace);
                return await Task.Run(() => daemon.RunExpression(expression, withPlan), cancellationToken);
            }
            catch (Exception ex)
            {
                var fallback = await RunExpressionJsonAsync(settings, expression, withPlan, cancellationToken);
                return new ExpressionRunResult(
                    fallback,
                    JsonLineParser.ParseFirstJsonLine<EvaluationJsonPayload>(fallback.Stdout),
                    usedDaemon: false,
                    daemonError: ex.Message);
            }
        }

        var result = await RunExpressionJsonAsync(settings, expression, withPlan, cancellationToken);
        return new ExpressionRunResult(
            result,
            JsonLineParser.ParseFirstJsonLine<EvaluationJsonPayload>(result.Stdout));
    }

    public async Task<CliRunResult> RunExpressionJsonAsync(
        EfvibeSettings settings,
        string expression,
        bool withPlan,
        CancellationToken cancellationToken)
    {
        var args = new List<string>(BuildBaseArgs(settings))
        {
            "-e",
            expression,
            "--format",
            "json",
            "--no-banner",
        };

        if (withPlan)
            args.Add("--with-plan");

        return await RunAsync(settings, args, TimeSpan.FromMinutes(10), cancellationToken);
    }

    public Task<CliRunResult> RunDbInfoJsonAsync(EfvibeSettings settings, CancellationToken cancellationToken) =>
        RunJsonFlagAsync(settings, "--dbinfo-json", cancellationToken);

    public Task<CliRunResult> RunTablesJsonAsync(EfvibeSettings settings, CancellationToken cancellationToken) =>
        RunJsonFlagAsync(settings, "--tables-json", cancellationToken);

    public Task<CliRunResult> RunAboutJsonAsync(EfvibeSettings settings, CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "--about-json",
            "--no-banner",
        };

        return RunAsync(settings, args, TimeSpan.FromSeconds(30), cancellationToken);
    }

    public Task<CliRunResult> RunDescribeJsonAsync(
        EfvibeSettings settings,
        string entityName,
        CancellationToken cancellationToken)
    {
        var args = new List<string>(BuildBaseArgs(settings))
        {
            "--describe-json",
            entityName,
            "--no-banner",
        };

        return RunAsync(settings, args, TimeSpan.FromMinutes(10), cancellationToken);
    }

    public Task<CliRunResult> RunScanAsync(
        EfvibeSettings settings,
        string mode,
        CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "scan",
            mode,
        };

        args.AddRange(BuildBaseArgs(settings));
        args.Add("--json");
        args.Add("--no-banner");

        if (settings.ScanRespectDismissals)
            args.Add("--respect-dismissals");

        AddOption(args, "--min-severity", settings.ScanMinSeverity);

        return RunAsync(settings, args, TimeSpan.FromMinutes(20), cancellationToken);
    }

    public Task<CliRunResult> RunScanNoteAsync(
        EfvibeSettings settings,
        ScanFinding finding,
        string note,
        CancellationToken cancellationToken)
    {
        var args = BuildScanFindingArgs(settings, "note", finding);
        args.Add("--text");
        args.Add(note);
        return RunAsync(settings, args, TimeSpan.FromSeconds(30), cancellationToken);
    }

    public Task<CliRunResult> RunScanDismissAsync(
        EfvibeSettings settings,
        ScanFinding finding,
        string? note,
        CancellationToken cancellationToken)
    {
        var args = BuildScanFindingArgs(settings, "dismiss", finding);
        if (!string.IsNullOrWhiteSpace(note))
        {
            args.Add("--note");
            args.Add(note.Trim());
        }

        return RunAsync(settings, args, TimeSpan.FromSeconds(30), cancellationToken);
    }

    public void StartReplInExternalTerminal(EfvibeSettings settings)
    {
        var commandLine = BuildReplCommandLine(settings);
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/k " + QuoteForCmd(commandLine),
            WorkingDirectory = _solutionDirectory,
            UseShellExecute = true,
        };

        Process.Start(startInfo);
    }

    private Task<CliRunResult> RunJsonFlagAsync(
        EfvibeSettings settings,
        string flag,
        CancellationToken cancellationToken)
    {
        var args = new List<string>(BuildBaseArgs(settings))
        {
            flag,
            "--no-banner",
        };

        return RunAsync(settings, args, TimeSpan.FromMinutes(10), cancellationToken);
    }

    private async Task<CliRunResult> RunAsync(
        EfvibeSettings settings,
        IReadOnlyList<string> args,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var invocation = ResolveInvocation(settings);
        var allArgs = new List<string>(invocation.PrefixArgs);
        allArgs.AddRange(args);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = invocation.Command,
            Arguments = BuildArguments(allArgs),
            WorkingDirectory = _solutionDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stderr.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        await WaitForExitAsync(process, timeoutSource.Token);

        return new CliRunResult
        {
            ExitCode = process.ExitCode,
            Stdout = stdout.ToString(),
            Stderr = stderr.ToString(),
        };
    }

    private static Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
    {
        if (process.HasExited)
            return Task.CompletedTask;

        var completion = new TaskCompletionSource<object?>();
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => completion.TrySetResult(null);

        cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (InvalidOperationException)
            {
            }

            completion.TrySetCanceled(cancellationToken);
        });

        return completion.Task;
    }

    private static void AddOption(ICollection<string> args, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        args.Add(name);
        args.Add(value.Trim());
    }

    private static string? FindDotnetToolsManifest(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);

        for (var depth = 0; current is not null && depth < 12; depth++)
        {
            var candidate = Path.Combine(current.FullName, ".config", "dotnet-tools.json");

            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        return null;
    }

    private static string BuildCommandLine(string command, IReadOnlyList<string> args) =>
        QuoteArg(command) + (args.Count == 0 ? string.Empty : " " + BuildArguments(args));

    internal static string BuildArguments(IEnumerable<string> args) =>
        string.Join(" ", args.Select(QuoteArg));

    private List<string> BuildScanFindingArgs(EfvibeSettings settings, string command, ScanFinding finding)
    {
        var args = new List<string> { "scan", command };
        AddOption(args, "-w", settings.WorkspaceRoot);
        AddOption(args, "-p", settings.Project);
        AddOption(args, "-c", settings.Context);
        AddOption(args, "--file", finding.FilePath);
        AddOption(args, "--line", finding.Line.ToString());
        AddOption(args, "--rule", finding.RuleId);
        AddOption(args, "--code", finding.Code);
        return args;
    }

    private static string QuoteArg(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        return value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0
            ? value
            : "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static string QuoteForCmd(string commandLine) =>
        "\"" + commandLine.Replace("\"", "\\\"") + "\"";
}
