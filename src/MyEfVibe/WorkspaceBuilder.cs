using System.Diagnostics;

namespace MyEfVibe;

internal static class WorkspaceBuilder
{
    internal static WorkspaceBuildResult Build(string workspaceDirectory, string? explicitProjectPathOrNull)
    {
        var projectFile =
            WorkspaceProjectLocator.ResolveProject(workspaceDirectory, explicitProjectPathOrNull);

        return BuildResolvedProject(workspaceDirectory, projectFile);
    }

    internal static WorkspaceBuildResult BuildResolvedProject(string workspaceDirectory, FileInfo projectFile)
    {
        RunDotnetBuild(projectFile.FullName);

        return WorkspaceBuildResult.RequirePrimaryAssembly(workspaceDirectory, projectFile);
    }

    private static void RunDotnetBuild(string csprojFullPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{csprojFullPath}\" -c Release --nologo -v q",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        using var buildProcess = Process.Start(startInfo);

        if (buildProcess is null)
            throw new WorkspaceException("Unable to launch the `dotnet` CLI. Ensure the .NET SDK is installed.");

        if (!buildProcess.WaitForExit((int)TimeSpan.FromMinutes(10).TotalMilliseconds))
        {
            buildProcess.Kill(entireProcessTree: true);

            throw new WorkspaceException("`dotnet build` timed out after 10 minutes.");
        }

        if (buildProcess.ExitCode == 0)
            return;

        var stderr = buildProcess.StandardError.ReadToEnd();
        var stdout = buildProcess.StandardOutput.ReadToEnd();

        throw new WorkspaceException(
            $"`dotnet build` failed (exit code {buildProcess.ExitCode}).{Environment.NewLine}{stderr}{stdout}");
    }
}
