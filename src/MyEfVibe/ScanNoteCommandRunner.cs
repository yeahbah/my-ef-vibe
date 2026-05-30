using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyEfVibe;

internal static class ScanNoteCommandRunner
{
    internal static Task<int> RunFromOptionsAsync(ScanNoteCliOptions options)
    {
        return RunAsync(
            CliPathHelper.ResolveWorkspace(options.Workspace),
            CliPathHelper.ToFileInfo(options.Project)!,
            options.Context,
            options.File,
            options.Line,
            options.Rule,
            options.Text,
            options.Code);
    }

    internal static Task<int> RunAsync(
        DirectoryInfo workspace,
        FileInfo projectPath,
        string? contextFullName,
        string filePath,
        int line,
        string ruleId,
        string noteText,
        string? code)
    {
        if (line <= 0)
        {
            CliUi.WriteError("--line must be a positive line number.");
            return Task.FromResult(2);
        }

        if (string.IsNullOrWhiteSpace(ruleId))
        {
            CliUi.WriteError("--rule is required.");
            return Task.FromResult(2);
        }

        if (string.IsNullOrWhiteSpace(noteText))
        {
            CliUi.WriteError("--text is required.");
            return Task.FromResult(2);
        }

        var workspaceRoot = SessionPaths.EnsureSessionDirectory(workspace.FullName);
        var sessionDirectory = ScanNoteCommandRunnerHelpers.ResolveSessionDirectory(
            workspaceRoot,
            projectPath.FullName,
            contextFullName);
        var finding = ScanNoteCommandRunnerHelpers.BuildFinding(filePath, line, ruleId, code);

        try
        {
            LinqScanNoteStore.SaveNote(sessionDirectory, finding, noteText);
        }
        catch (ArgumentException failure)
        {
            CliUi.WriteError(failure.Message);
            return Task.FromResult(2);
        }

        ScanNoteCommandRunnerHelpers.WriteSuccess("note", finding.GetDismissalKey(), sessionDirectory);
        return Task.FromResult(0);
    }
}

internal static class ScanDismissCommandRunner
{
    internal static Task<int> RunFromOptionsAsync(ScanDismissCliOptions options)
    {
        return RunAsync(
            CliPathHelper.ResolveWorkspace(options.Workspace),
            CliPathHelper.ToFileInfo(options.Project)!,
            options.Context,
            options.File,
            options.Line,
            options.Rule,
            options.Note,
            options.Code);
    }

    internal static Task<int> RunAsync(
        DirectoryInfo workspace,
        FileInfo projectPath,
        string? contextFullName,
        string filePath,
        int line,
        string ruleId,
        string? dismissalNote,
        string? code)
    {
        if (line <= 0)
        {
            CliUi.WriteError("--line must be a positive line number.");
            return Task.FromResult(2);
        }

        if (string.IsNullOrWhiteSpace(ruleId))
        {
            CliUi.WriteError("--rule is required.");
            return Task.FromResult(2);
        }

        var workspaceRoot = SessionPaths.EnsureSessionDirectory(workspace.FullName);
        var sessionDirectory = ScanNoteCommandRunnerHelpers.ResolveSessionDirectory(
            workspaceRoot,
            projectPath.FullName,
            contextFullName);
        var finding = ScanNoteCommandRunnerHelpers.BuildFinding(filePath, line, ruleId, code);

        LinqScanDismissalStore.Dismiss(sessionDirectory, finding, dismissalNote);

        ScanNoteCommandRunnerHelpers.WriteSuccess("dismiss", finding.GetDismissalKey(), sessionDirectory);
        return Task.FromResult(0);
    }
}

internal static class ScanNoteCommandRunnerHelpers
{
    internal static string ResolveSessionDirectory(
        string workspaceRoot,
        string projectPath,
        string? contextFullName)
    {
        if (!string.IsNullOrWhiteSpace(contextFullName))
        {
            return SessionPaths.EnsureDbContextSessionDirectory(
                workspaceRoot,
                projectPath,
                contextFullName.Trim());
        }

        return SessionPaths.EnsureProjectScanDirectory(workspaceRoot, projectPath);
    }

    internal static LinqScanFinding BuildFinding(string filePath, int line, string ruleId, string? code)
    {
        var fullPath = Path.GetFullPath(filePath);

        return LinqScanFinding.Create(
            fullPath,
            line,
            code ?? string.Empty,
            ruleId.Trim(),
            ruleId.Trim());
    }

    internal static void WriteSuccess(string action, string key, string sessionDirectory)
    {
        var payload = new ScanActionJsonPayload
        {
            Success = true,
            Action = action,
            Key = key,
            SessionDirectory = sessionDirectory
        };

        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        }));
    }
}

internal sealed class ScanActionJsonPayload
{
    public bool Success { get; init; }

    public string Action { get; init; } = string.Empty;

    public string Key { get; init; } = string.Empty;

    public string SessionDirectory { get; init; } = string.Empty;
}