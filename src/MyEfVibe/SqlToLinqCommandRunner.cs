using MyEfVibe.Workspace;
using Spectre.Console;

namespace MyEfVibe;

internal static class SqlToLinqCommandRunner
{
    internal static Task<int> RunFromOptionsAsync(
        SqlToLinqCliOptions options,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            options.Sql,
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
            string.Equals(options.Format, "json", StringComparison.OrdinalIgnoreCase),
            options.NoBanner,
            cancellationToken);
    }

    internal static async Task<int> RunAsync(
        string sql,
        DirectoryInfo workspace,
        FileInfo? projectPath,
        FileInfo? startupProjectPath,
        string? contextFullName,
        string? connectionString,
        bool dbLogEnabled,
        bool noDbLog,
        string? dbLogLevelRaw,
        bool dbLogVerbose,
        string? frameworkOrNull,
        bool jsonOutput,
        bool noBanner,
        CancellationToken cancellationToken = default)
    {
        CliUi.Configure();

        var (runtime, exitCode, error) = await WorkspaceRuntimeBootstrap.LoadAsync(
            workspace,
            projectPath,
            startupProjectPath,
            contextFullName,
            connectionString,
            dbLogEnabled,
            noDbLog,
            dbLogLevelRaw,
            dbLogVerbose,
            frameworkOrNull,
            cancellationToken);

        if (runtime is null)
        {
            if (jsonOutput)
            {
                Console.Error.WriteLine(error ?? "Failed to load workspace.");
            }
            else
            {
                CliUi.WriteErrorPanel("Workspace failure", error ?? "Failed to load workspace.");
            }

            return exitCode;
        }

        using (runtime)
        {
            var draft = await SqlToLinqService.ConvertAndValidateAsync(
                runtime.DbContext,
                runtime.Session,
                runtime.Host.EnumerateLoadedAssemblies(),
                runtime.DbLogSettings,
                sql,
                cancellationToken);

            if (jsonOutput || noBanner)
            {
                SqlToLinqJsonReporter.Write(draft);
                return 0;
            }

            AnsiConsole.MarkupLine($"[bold]Confidence[/]: {Markup.Escape(draft.Confidence)}");

            if (draft.Mappings.Count > 0)
            {
                AnsiConsole.MarkupLine(
                    "[dim]Mappings[/]: "
                    + Markup.Escape(string.Join(", ", draft.Mappings.Select(mapping => $"{mapping.Table} -> {mapping.DbSet}"))));
            }

            foreach (var item in draft.Unsupported)
            {
                CliUi.WriteWarning(item);
            }

            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine(draft.Linq);

            if (!string.IsNullOrWhiteSpace(draft.TranslatedSql))
            {
                AnsiConsole.WriteLine();
                CliUi.WriteSqlBlock("Round-tripped SQL (ToQueryString)", draft.TranslatedSql);

                if (draft.Similarity is not null)
                {
                    AnsiConsole.MarkupLine($"[dim]Similarity[/]: {draft.Similarity:0.00}");
                }
            }

            return 0;
        }
    }
}
