using MyEfVibe.Reporters;
using MyEfVibe.Workspace;

namespace MyEfVibe;

internal static class ServeCommandRunner
{
    internal static async Task<int> RunFromOptionsAsync(
        ServeCliOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.Project))
        {
            ServeProtocol.WriteError("serve requires --project (-p).");
            return 1;
        }

        var (runtime, exitCode, error) = await WorkspaceRuntimeBootstrap.LoadAsync(
            CliPathHelper.ResolveWorkspace(options.Workspace),
            CliPathHelper.ToFileInfo(options.Project),
            CliPathHelper.ToFileInfo(options.StartupProject),
            options.Context,
            options.ConnectionString,
            options.DbLog,
            options.NoDbLog,
            options.DbLogLevel,
            options.DbLogVerbose,
            options.Framework,
            cancellationToken);

        if (runtime is null)
        {
            ServeProtocol.WriteError(error ?? "Failed to load workspace.");
            return exitCode;
        }

        using (runtime)
        {
            ServeProtocol.WriteReady(runtime);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await Console.In.ReadLineAsync(cancellationToken);

                if (line is null)
                {
                    break;
                }

                var request = ServeProtocol.TryParseRequest(line);

                if (request is null || string.IsNullOrWhiteSpace(request.Type))
                {
                    ServeProtocol.WriteError(
                        "Invalid request JSON. Expected {\"type\":\"eval|dbinfo|tables|describe|scan|completions|ping|shutdown\",...}.");
                    continue;
                }

                switch (request.Type.ToLowerInvariant())
                {
                    case "shutdown":
                    case "exit":
                        return 0;

                    case "ping":
                        ServeProtocol.WritePong();
                        break;

                    case "eval":
                        if (string.IsNullOrWhiteSpace(request.Expression))
                        {
                            ServeProtocol.WriteError("eval requires \"expression\".");
                            break;
                        }

                        await ServeEvaluator.EvaluateAndWriteJsonAsync(
                            runtime,
                            request.Expression,
                            request.WithPlan,
                            cancellationToken);

                        break;

                    case "dbinfo":
                        await DbInfoJsonReporter.WriteAsync(runtime.DbContext, runtime.Host, cancellationToken);
                        break;

                    case "tables":
                        TablesJsonReporter.Write(runtime.DbContext);
                        break;

                    case "describe":
                        if (string.IsNullOrWhiteSpace(request.Entity))
                        {
                            ServeProtocol.WriteError("describe requires \"entity\".");
                            break;
                        }

                        DescribeJsonReporter.Write(runtime.DbContext, request.Entity);
                        break;

                    case "scan":
                        if (string.IsNullOrWhiteSpace(request.Mode))
                        {
                            ServeProtocol.WriteError("scan requires \"mode\" (lite or deep).");
                            break;
                        }

                        try
                        {
                            await ServeScanner.WriteJsonScanAsync(
                                runtime,
                                request.Mode,
                                request.RespectDismissals,
                                request.MinSeverity,
                                cancellationToken);
                        }
                        catch (Exception failure)
                        {
                            ServeProtocol.WriteError(failure.Message);
                        }

                        break;

                    case "completions":
                        if (request.Prefix is null)
                        {
                            ServeProtocol.WriteError("completions requires \"prefix\".");
                            break;
                        }

                        CompletionsJsonReporter.Write(runtime.DbContext, request.Prefix);
                        break;

                    default:
                        ServeProtocol.WriteError($"Unknown request type '{request.Type}'.");
                        break;
                }
            }
        }

        return 0;
    }
}