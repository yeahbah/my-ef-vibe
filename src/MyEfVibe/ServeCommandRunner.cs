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

        WorkspaceBuildPolicy buildPolicy;
        try
        {
            buildPolicy = WorkspaceBuildPolicyResolver.Resolve(options.NoBuild, options.ForceBuild);
        }
        catch (WorkspaceException failure)
        {
            ServeProtocol.WriteError(failure.Message);
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
            buildPolicy,
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
                        "Invalid request JSON. Expected {\"type\":\"eval|dbinfo|tables|describe|scan|completions|sqlToLinq|executeSql|applyResultChanges|ping|shutdown\",...}.");
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

                    case "sqltolinq":
                        if (string.IsNullOrWhiteSpace(request.Sql))
                        {
                            ServeProtocol.WriteError("sqlToLinq requires \"sql\".");
                            break;
                        }

                        try
                        {
                            var draft = await SqlToLinqService.ConvertAndValidateAsync(
                                runtime.DbContext,
                                runtime.Session,
                                runtime.Host.EnumerateLoadedAssemblies(),
                                runtime.DbLogSettings,
                                request.Sql,
                                cancellationToken);

                            SqlToLinqJsonReporter.Write(draft);
                        }
                        catch (Exception failure)
                        {
                            ServeProtocol.WriteError(failure.Message);
                        }

                        break;

                    case "executesql":
                        if (string.IsNullOrWhiteSpace(request.Sql))
                        {
                            ServeProtocol.WriteError("executeSql requires \"sql\".");
                            break;
                        }

                        await ServeSqlEvaluator.ExecuteAndWriteJsonAsync(
                            runtime,
                            request.Sql,
                            request.WithPlan,
                            cancellationToken);

                        break;

                    case "applyresultchanges":
                        if (string.IsNullOrWhiteSpace(request.Entity))
                        {
                            ServeProtocol.WriteError("applyResultChanges requires \"entity\".");
                            break;
                        }

                        await ServeResultChangesApplier.ApplyAndWriteJsonAsync(
                            runtime,
                            request.Entity,
                            ApplyResultChangesJsonReporter.ParseChanges(request.Updates),
                            ApplyResultChangesJsonReporter.ParseChanges(request.Deletes),
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