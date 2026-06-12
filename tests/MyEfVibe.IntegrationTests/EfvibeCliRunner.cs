using System.Diagnostics;
using System.Text;

namespace MyEfVibe.IntegrationTests;

internal sealed record EfvibeCliResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    internal string CombinedOutput => StandardOutput + StandardError;
}

internal static class EfvibeCliRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    internal static string ExecutablePath =>
        Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "myefvibe.exe" : "myefvibe");

    internal static async Task<EfvibeCliResult> RunAsync(
        IEnumerable<string> arguments,
        TimeSpan? timeout = null,
        Func<string, bool>? stopWhen = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ExecutablePath))
        {
            throw new FileNotFoundException($"myefvibe executable not found: {ExecutablePath}");
        }

        timeout ??= DefaultTimeout;

        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stopTriggered = false;

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            stdout.AppendLine(eventArgs.Data);

            if (!stopTriggered && stopWhen?.Invoke(eventArgs.Data) == true)
            {
                stopTriggered = true;

                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Process may already be exiting.
                }
            }
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                stderr.AppendLine(eventArgs.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start {ExecutablePath}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Process may already be exiting.
            }
        });

        var completed = await Task.Run(
            () => process.WaitForExit((int)timeout.Value.TotalMilliseconds),
            cancellationToken);

        if (!completed && !process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process may already be exiting.
            }

            process.WaitForExit();
        }

        return new EfvibeCliResult(
            process.HasExited ? process.ExitCode : -1,
            stdout.ToString(),
            stderr.ToString());
    }

    internal static void AssertOptionRecognized(EfvibeCliResult result)
    {
        Assert.DoesNotContain("is unknown", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UnknownOptionError", result.CombinedOutput, StringComparison.OrdinalIgnoreCase);
    }
}
