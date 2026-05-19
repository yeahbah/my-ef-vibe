using System.Diagnostics;

namespace MyEfVibe;

internal static class WorkspaceBuilder
{
    internal static WorkspaceBuildResult Build(
        string sessionDirectory,
        string searchDirectory,
        string? explicitProjectPathOrNull,
        string? explicitStartupPathOrNull)
    {
        var projectFile = WorkspaceProjectLocator.ResolveProject(searchDirectory, explicitProjectPathOrNull);
        var startupProject = StartupProjectResolver.Resolve(searchDirectory, projectFile, explicitStartupPathOrNull);

        return BuildResolvedProject(sessionDirectory, projectFile, startupProject);
    }

    internal static WorkspaceBuildResult BuildResolvedProject(
        string sessionDirectory,
        FileInfo projectFile,
        FileInfo startupProject)
    {
        RunDotnetBuild(projectFile.FullName);

        if (!string.Equals(projectFile.FullName, startupProject.FullName, StringComparison.OrdinalIgnoreCase)
            && !WorkspaceBuildResult.TryLocateStartupOutput(startupProject.FullName, out _))
        {
            RunDotnetBuild(startupProject.FullName);
        }

        return WorkspaceBuildResult.RequirePrimaryAssembly(sessionDirectory, projectFile, startupProject);
    }

    internal static void RunDotnetBuild(string csprojFullPath)
    {
        var frameworkArg = HostRuntimeFramework.PreferredOutputFolderName() is { } tfm
            ? $" -f {tfm}"
            : string.Empty;

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{csprojFullPath}\" -c Release --nologo -v q{frameworkArg}",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        using var buildProcess = Process.Start(startInfo);

        if (buildProcess is null)
            throw new WorkspaceException("Unable to launch the `dotnet` CLI. Ensure the .NET SDK is installed.");

        // Start draining pipes before WaitForExit — otherwise MSBuild/dotnet can fill the 4 KB buffer
        // and block while the parent waits for exit (classic Process redirect deadlock).
        var stdoutTask = buildProcess.StandardOutput.ReadToEndAsync();
        var stderrTask = buildProcess.StandardError.ReadToEndAsync();

        if (!buildProcess.WaitForExit((int)TimeSpan.FromMinutes(10).TotalMilliseconds))
        {
            try
            {
                buildProcess.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new WorkspaceException("`dotnet build` timed out after 10 minutes.");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        if (buildProcess.ExitCode == 0)
            return;

        throw new WorkspaceException(
            $"`dotnet build` failed (exit code {buildProcess.ExitCode}).{Environment.NewLine}{stderr}{stdout}");
    }
}
