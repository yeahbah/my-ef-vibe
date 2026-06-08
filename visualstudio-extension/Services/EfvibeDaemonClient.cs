using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MyEfVibe.VisualStudio.Models;

namespace MyEfVibe.VisualStudio.Services;

internal sealed class EfvibeDaemonClient : IDisposable
{
    private static readonly ConcurrentDictionary<string, EfvibeDaemonClient> Clients = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _lifecycleLock = new();
    private readonly SemaphoreSlim _requestSerializer = new(1, 1);
    private readonly string _sessionKey;
    private readonly Func<CliInvocationSpec> _buildServeSpec;

    private DaemonState? _daemon;
    private bool _disposed;

    private EfvibeDaemonClient(string sessionKey, Func<CliInvocationSpec> buildServeSpec)
    {
        _sessionKey = sessionKey;
        _buildServeSpec = buildServeSpec;
    }

    internal static EfvibeDaemonClient GetOrCreate(EfvibeWorkspace.WorkspaceContext context)
    {
        var key = BuildSessionKey(context);
        return Clients.GetOrAdd(
            key,
            _ => new EfvibeDaemonClient(key, () => context.Runner.BuildServeSpec(context.Settings)));
    }

    internal static void InvalidateAll() =>
        InvalidateMatching(_ => true);

    internal static void InvalidateFor(EfvibeWorkspace.WorkspaceContext context) =>
        InvalidateMatching(key => string.Equals(key, BuildSessionKey(context), StringComparison.Ordinal));

    internal bool IsReady()
    {
        lock (_lifecycleLock)
            return _daemon is { Ready: true, Process: { HasExited: false } };
    }

    internal ExpressionRunResult RunExpression(string expression, bool withPlan)
    {
        _requestSerializer.Wait();
        try
        {
            lock (_lifecycleLock)
            {
                var state = EnsureStartedLocked();
                var request = JsonSerializer.Serialize(new
                {
                    type = "eval",
                    expression,
                    withPlan,
                });

                state.WriteLine(request);
                var line = state.WaitForLine(TimeSpan.FromMinutes(10))
                    ?? throw new InvalidOperationException("efvibe daemon timed out waiting for an evaluation response.");

                var messageType = ParseMessageType(line);
                if (messageType == "error")
                {
                    throw new InvalidOperationException(ParseMessageText(line) ?? "efvibe daemon error.");
                }

                var payload = JsonLineParser.ParseFirstJsonLine<EvaluationJsonPayload>(line);
                var exitCode = payload?.Success == true ? 0 : 20;
                return new ExpressionRunResult(
                    new CliRunResult { ExitCode = exitCode, Stdout = line, Stderr = string.Empty },
                    payload,
                    usedDaemon: true);
            }
        }
        finally
        {
            _requestSerializer.Release();
        }
    }

    public void Dispose()
    {
        _requestSerializer.Wait();
        try
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                StopLocked();
            }
        }
        finally
        {
            _requestSerializer.Release();
            _requestSerializer.Dispose();
        }
    }

    private static void InvalidateMatching(Func<string, bool> predicate)
    {
        foreach (var pair in Clients)
        {
            if (!predicate(pair.Key))
                continue;

            if (Clients.TryRemove(pair.Key, out var client))
                client.Dispose();
        }
    }

    private DaemonState EnsureStartedLocked()
    {
        if (_daemon is { Ready: true, Process: { HasExited: false } })
            return _daemon;

        StopLocked();

        var spec = _buildServeSpec();
        var startInfo = new ProcessStartInfo
        {
            FileName = spec.Command,
            Arguments = CliRunner.BuildArguments(spec.Args),
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start efvibe serve.");

        var state = new DaemonState(process);
        _daemon = state;

        var deadline = DateTime.UtcNow.AddMinutes(10);
        while (DateTime.UtcNow < deadline)
        {
            var line = state.WaitForLine(TimeSpan.FromSeconds(1));
            if (line is null)
                continue;

            var messageType = ParseMessageType(line);
            if (messageType == "ready")
            {
                state.Ready = true;
                return state;
            }

            if (messageType == "error")
            {
                StopLocked();
                throw new InvalidOperationException(ParseMessageText(line) ?? "efvibe serve failed to start.");
            }
        }

        var details = state.StderrSummary();
        StopLocked();
        throw new InvalidOperationException(
            "efvibe serve timed out during workspace load." + details);
    }

    private void StopLocked()
    {
        if (_daemon is null)
            return;

        try
        {
            if (!_daemon.Process.HasExited)
                _daemon.Process.Kill();
        }
        catch (InvalidOperationException)
        {
        }

        _daemon.Dispose();
        _daemon = null;
    }

    private static string BuildSessionKey(EfvibeWorkspace.WorkspaceContext context) =>
        string.Join(
            "|",
            context.SolutionDirectory,
            context.Settings.WorkspaceRoot,
            context.Settings.Project,
            context.Settings.StartupProject,
            context.Settings.Context,
            context.Settings.ConnectionString,
            context.Settings.ToolPath,
            context.Settings.DotnetFramework,
            context.Settings.DbLog.ToString());

    private static string? ParseMessageType(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.TryGetProperty("type", out var type)
                ? type.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ParseMessageText(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.TryGetProperty("message", out var message)
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class DaemonState : IDisposable
    {
        private readonly BlockingCollection<string> _lines = new();
        private readonly StringBuilder _buffer = new();
        private readonly StringBuilder _stderr = new();

        internal DaemonState(Process process)
        {
            Process = process;
            Process.OutputDataReceived += OnOutputDataReceived;
            Process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    _stderr.AppendLine(e.Data);
            };
            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();
        }

        internal Process Process { get; }
        internal bool Ready { get; set; }

        internal void WriteLine(string line)
        {
            if (Process.HasExited)
                throw new InvalidOperationException("efvibe daemon process has exited.");

            Process.StandardInput.WriteLine(line);
            Process.StandardInput.Flush();
        }

        internal string? WaitForLine(TimeSpan timeout) =>
            _lines.TryTake(out var line, (int)timeout.TotalMilliseconds) ? line : null;

        internal string StderrSummary()
        {
            var text = _stderr.ToString().Trim();
            return string.IsNullOrWhiteSpace(text)
                ? string.Empty
                : Environment.NewLine + "stderr:" + Environment.NewLine + text.Substring(0, Math.Min(text.Length, 4000));
        }

        public void Dispose()
        {
            _lines.CompleteAdding();
            _lines.Dispose();
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is null)
                return;

            lock (_buffer)
            {
                _buffer.Append(e.Data);
                while (true)
                {
                    var text = _buffer.ToString();
                    var index = text.IndexOf('\n');
                    if (index < 0)
                        break;

                    var line = text.Substring(0, index).Trim('\r', '\n', ' ');
                    _buffer.Clear();
                    _buffer.Append(text.Substring(index + 1));

                    if (line.Length > 0)
                        _lines.Add(line);
                }
            }
        }
    }
}

internal sealed class CliInvocationSpec
{
    public string Command { get; set; } = string.Empty;
    public string[] Args { get; set; } = Array.Empty<string>();
    public string WorkingDirectory { get; set; } = string.Empty;
}
