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
            options.Provider,
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
                    break;

                var request = ServeProtocol.TryParseRequest(line);

                if (request is null || string.IsNullOrWhiteSpace(request.Type))
                {
                    ServeProtocol.WriteError("Invalid request JSON. Expected {\"type\":\"eval|ping|shutdown\",...}.");
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

                    default:
                        ServeProtocol.WriteError($"Unknown request type '{request.Type}'.");
                        break;
                }
            }
        }

        return 0;
    }
}
