using System.Text.Json;

namespace MyEfVibe.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class CliSwitchIntegrationTests
{
    private static readonly IntegrationScenario SqliteScenario = IntegrationScenarioCatalog.Require("sqlite");

    public static TheoryData<CliCommandKind, string, string[]> SwitchCases
    {
        get
        {
            var data = new TheoryData<CliCommandKind, string, string[]>();

            foreach (var switchCase in CliSwitchCatalog.All())
            {
                data.Add(switchCase.Command, switchCase.Name, switchCase.ExtraArguments.ToArray());
            }

            return data;
        }
    }

    [SkippableFact]
    public async Task About_json_runs_without_workspace()
    {
        IntegrationTestGuards.RequireEnabled();

        var result = await EfvibeCliRunner.RunAsync(["--about-json"], TimeSpan.FromSeconds(30));

        EfvibeCliRunner.AssertOptionRecognized(result);
        Assert.Equal(0, result.ExitCode);

        using var document = JsonDocument.Parse(result.StandardOutput.Trim());
        Assert.True(document.RootElement.TryGetProperty("toolVersion", out _));
        Assert.True(document.RootElement.TryGetProperty("command", out var command));
        Assert.Equal("efvibe", command.GetString());
    }

    [SkippableTheory]
    [MemberData(nameof(SwitchCases))]
    public async Task Cli_switch_is_recognized(CliCommandKind command, string _, string[] extraArguments)
    {
        IntegrationTestGuards.RequireEnabled();
        Skip.IfNot(
            DatabaseProbe.TryValidateScenario(SqliteScenario, out var validationFailure),
            validationFailure ?? "sqlite scenario paths are invalid.");

        var arguments = CliSwitchArgumentBuilder.BuildParsingCommand(command, SqliteScenario, extraArguments);
        var timeout = command is CliCommandKind.Serve
                or CliCommandKind.LanguageServer
                or CliCommandKind.ScanDeep
            ? TimeSpan.FromSeconds(30)
            : command is CliCommandKind.ScanLite
                ? TimeSpan.FromMinutes(2)
                : TimeSpan.FromSeconds(30);

        var result = await EfvibeCliRunner.RunAsync(arguments, timeout);

        EfvibeCliRunner.AssertOptionRecognized(result);
    }

    [SkippableFact]
    public async Task Main_tables_json_emits_db_sets()
    {
        IntegrationTestGuards.RequireEnabled();
        await AssertJsonCommandAsync(
            BuildSqliteCommand(
                "--tables-json",
                "--no-banner",
                "--connection-string",
                SqliteScenario.ConnectionString!),
            "dbContext");
    }

    [SkippableFact]
    public async Task Main_dbinfo_json_emits_connection_metadata()
    {
        IntegrationTestGuards.RequireEnabled();
        await AssertJsonCommandAsync(
            BuildSqliteCommand(
                "--dbinfo-json",
                "--no-banner",
                "--connection-string",
                SqliteScenario.ConnectionString!),
            "entries");
    }

    [SkippableFact]
    public async Task Main_describe_json_emits_entity_members()
    {
        IntegrationTestGuards.RequireEnabled();
        await AssertJsonCommandAsync(
            BuildSqliteCommand(
                "--describe-json",
                "Products",
                "--no-banner",
                "--connection-string",
                SqliteScenario.ConnectionString!),
            "members");
    }

    [SkippableFact]
    public async Task Main_completions_json_emits_items()
    {
        IntegrationTestGuards.RequireEnabled();
        await AssertJsonCommandAsync(
            BuildSqliteCommand(
                "--completions-json",
                "db.Pro",
                "--no-banner",
                "--connection-string",
                SqliteScenario.ConnectionString!),
            "items");
    }

    [SkippableFact]
    public async Task Main_expression_json_runs_query()
    {
        IntegrationTestGuards.RequireEnabled();

        var result = await EfvibeCliRunner.RunAsync(
            BuildSqliteCommand(
                "-e",
                "db.Products.Count()",
                "--format",
                "json",
                "--no-banner",
                "--connection-string",
                SqliteScenario.ConnectionString!),
            TimeSpan.FromMinutes(2));

        EfvibeCliRunner.AssertOptionRecognized(result);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"success\":true", result.StandardOutput, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Main_positional_expression_runs_query()
    {
        IntegrationTestGuards.RequireEnabled();

        var result = await EfvibeCliRunner.RunAsync(
            BuildSqliteCommand(
                "--format",
                "json",
                "--no-banner",
                "--connection-string",
                SqliteScenario.ConnectionString!,
                "db.Products.Count()"),
            TimeSpan.FromMinutes(2));

        EfvibeCliRunner.AssertOptionRecognized(result);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"success\":true", result.StandardOutput, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Main_with_plan_includes_query_plan()
    {
        IntegrationTestGuards.RequireEnabled();

        var result = await EfvibeCliRunner.RunAsync(
            BuildSqliteCommand(
                "-e",
                "db.Products.Take(1).ToList()",
                "--format",
                "json",
                "--with-plan",
                "--no-banner",
                "--connection-string",
                SqliteScenario.ConnectionString!),
            TimeSpan.FromMinutes(2));

        EfvibeCliRunner.AssertOptionRecognized(result);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"queryPlan\"", result.StandardOutput, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Scan_lite_json_emits_summary()
    {
        IntegrationTestGuards.RequireEnabled();

        var result = await EfvibeCliRunner.RunAsync(
            CliSwitchArgumentBuilder.BuildParsingCommand(
                CliCommandKind.ScanLite,
                SqliteScenario,
                ["--min-severity", "warning", "--fail-on", "critical"]),
            TimeSpan.FromMinutes(2));

        EfvibeCliRunner.AssertOptionRecognized(result);
        Assert.Contains("\"scanMode\":\"lite\"", result.StandardOutput, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Scan_deep_json_emits_summary()
    {
        IntegrationTestGuards.RequireEnabled();

        var result = await EfvibeCliRunner.RunAsync(
            BuildSqliteScanDeepCommand("--respect-dismissals"),
            TimeSpan.FromMinutes(3));

        EfvibeCliRunner.AssertOptionRecognized(result);
        Assert.Contains("\"scanMode\":\"deep\"", result.StandardOutput, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Serve_emits_ready_with_switches()
    {
        IntegrationTestGuards.RequireEnabled();

        var workspace = CreateWorkspaceDirectory();
        var result = await EfvibeCliRunner.RunAsync(
            BuildSqliteServeCommand(
                "-w", workspace,
                "--no-dblog",
                "--dblog-level", "warning",
                "--connection-string", SqliteScenario.ConnectionString!),
            TimeSpan.FromMinutes(3),
            static line => line.Contains("\"type\":\"ready\"", StringComparison.Ordinal));

        EfvibeCliRunner.AssertOptionRecognized(result);
        Assert.Contains("\"type\":\"ready\"", result.StandardOutput, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Connection_string_long_form_is_recognized_on_main_command()
    {
        IntegrationTestGuards.RequireEnabled();

        var result = await EfvibeCliRunner.RunAsync(
            ["--about-json", "--connection-string", SqliteScenario.ConnectionString!],
            TimeSpan.FromSeconds(30));

        EfvibeCliRunner.AssertOptionRecognized(result);
        Assert.Equal(0, result.ExitCode);
    }

    private static async Task AssertJsonCommandAsync(IReadOnlyList<string> arguments, string expectedProperty)
    {
        var result = await EfvibeCliRunner.RunAsync(arguments, TimeSpan.FromMinutes(2));

        EfvibeCliRunner.AssertOptionRecognized(result);
        Assert.Equal(0, result.ExitCode);

        using var document = JsonDocument.Parse(result.StandardOutput.Trim());
        Assert.True(document.RootElement.TryGetProperty(expectedProperty, out _));
    }

    private static IReadOnlyList<string> BuildSqliteCommand(params string[] arguments)
    {
        var command = new List<string>
        {
            "-p", SqliteScenario.EfProjectPath,
            "-s", SqliteScenario.StartupProjectPath,
            "-c", SqliteScenario.Context,
            "-f", SqliteScenario.Framework
        };

        command.AddRange(arguments);
        return command;
    }

    private static IReadOnlyList<string> BuildSqliteServeCommand(params string[] arguments)
    {
        var command = new List<string>
        {
            "serve",
            "-p", SqliteScenario.EfProjectPath,
            "-s", SqliteScenario.StartupProjectPath,
            "-c", SqliteScenario.Context,
            "-f", SqliteScenario.Framework
        };

        command.AddRange(arguments);
        return command;
    }

    private static IReadOnlyList<string> BuildSqliteScanDeepCommand(params string[] arguments)
    {
        var command = new List<string>
        {
            "scan", "deep",
            "-p", SqliteScenario.EfProjectPath,
            "-s", SqliteScenario.StartupProjectPath,
            "-c", SqliteScenario.Context,
            "--connection-string", SqliteScenario.ConnectionString!,
            "--json", "--no-banner"
        };

        command.AddRange(arguments);
        return command;
    }

    private static string CreateWorkspaceDirectory()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "efvibe-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        return workspace;
    }
}

internal static class CliSwitchArgumentBuilder
{
    private const string InvalidProjectPath = "/nonexistent/efvibe-cli-parse-test.csproj";
    private const string InvalidStartupPath = "/nonexistent/efvibe-cli-parse-startup.csproj";

    internal static IReadOnlyList<string> BuildParsingCommand(
        CliCommandKind command,
        IntegrationScenario scenario,
        IReadOnlyList<string> extraArguments)
    {
        var arguments = new List<string>();
        var workspace = Path.Combine(Path.GetTempPath(), "efvibe-cli-parse");
        var scanFile = Path.Combine(scenario.RepoRoot, "README.md");

        switch (command)
        {
            case CliCommandKind.Main:
                arguments.Add("--about-json");
                break;

            case CliCommandKind.Serve:
                arguments.AddRange(["serve", "-p", InvalidProjectPath]);
                break;

            case CliCommandKind.ScanLite:
                arguments.AddRange(["scan", "lite", "-p", scenario.EfProjectPath, "--json", "--no-banner"]);
                break;

            case CliCommandKind.ScanDeep:
                arguments.AddRange(
                [
                    "scan", "deep",
                    "-p", InvalidProjectPath,
                    "-s", InvalidStartupPath,
                    "-c", scenario.Context,
                    "--connection-string", scenario.ConnectionString ?? "invalid",
                    "--json", "--no-banner"
                ]);
                break;

            case CliCommandKind.ScanNote:
                arguments.AddRange(
                [
                    "scan", "note",
                    "-p", scenario.EfProjectPath,
                    "--file", scanFile,
                    "--line", "1",
                    "--rule", "integration-test",
                    "--text", "integration note"
                ]);
                break;

            case CliCommandKind.ScanDismiss:
                arguments.AddRange(
                [
                    "scan", "dismiss",
                    "-p", scenario.EfProjectPath,
                    "--file", scanFile,
                    "--line", "1",
                    "--rule", "integration-test"
                ]);
                break;

            case CliCommandKind.LanguageServer:
                arguments.AddRange(["language-server", "-p", InvalidProjectPath]);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, "Unsupported CLI command.");
        }

        arguments.AddRange(ResolveArguments(extraArguments, workspace, scenario, scanFile));
        return arguments;
    }

    private static IEnumerable<string> ResolveArguments(
        IReadOnlyList<string> extraArguments,
        string workspace,
        IntegrationScenario scenario,
        string scanFile)
    {
        foreach (var argument in extraArguments)
        {
            yield return argument switch
            {
                CliSwitchCatalog.Placeholders.Workspace => workspace,
                CliSwitchCatalog.Placeholders.EfProject => scenario.EfProjectPath,
                CliSwitchCatalog.Placeholders.StartupProject => scenario.StartupProjectPath,
                CliSwitchCatalog.Placeholders.Context => scenario.Context,
                CliSwitchCatalog.Placeholders.ConnectionString => scenario.ConnectionString ?? string.Empty,
                CliSwitchCatalog.Placeholders.Framework => scenario.Framework,
                CliSwitchCatalog.Placeholders.ScanFile => scanFile,
                _ => argument
            };
        }
    }
}
