using MyEfVibe.VisualStudio.Models;

namespace MyEfVibe.VisualStudio.Services;

internal sealed class ExpressionRunResult
{
    public ExpressionRunResult(
        CliRunResult result,
        EvaluationJsonPayload? payload,
        bool usedDaemon = false,
        string? daemonError = null)
    {
        Result = result;
        Payload = payload;
        UsedDaemon = usedDaemon;
        DaemonError = daemonError;
    }

    public CliRunResult Result { get; }
    public EvaluationJsonPayload? Payload { get; }
    public bool UsedDaemon { get; }
    public string? DaemonError { get; }
}
