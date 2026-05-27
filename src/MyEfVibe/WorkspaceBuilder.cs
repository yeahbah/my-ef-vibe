using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace MyEfVibe;

internal static class WorkspaceBuilder
{
    internal static WorkspaceBuildResult Build(
        string sessionDirectory,
        string searchDirectory,
        string? explicitProjectPathOrNull,
        string? explicitStartupPathOrNull,
        string? frameworkOrNull = null)
    {
        var projectFile = WorkspaceProjectLocator.ResolveProject(searchDirectory, explicitProjectPathOrNull);
        var startupProject = StartupProjectResolver.Resolve(searchDirectory, projectFile, explicitStartupPathOrNull);

        return BuildResolvedProject(sessionDirectory, projectFile, startupProject, frameworkOrNull);
    }

    internal static WorkspaceBuildResult BuildResolvedProject(
        string sessionDirectory,
        FileInfo projectFile,
        FileInfo startupProject,
        string? frameworkOrNull)
    {
        var projectFramework = ProjectTargetFrameworkResolver.ResolveBuildFramework(
            projectFile.FullName,
            frameworkOrNull);

        var projectOutput = GetIsolatedBuildOutput(sessionDirectory, projectFile.FullName, projectFramework);
        RunDotnetBuild(projectFile.FullName, projectFramework, projectOutput);

        ProjectBuildOutput? startupOutput = null;

        if (!string.Equals(projectFile.FullName, startupProject.FullName, StringComparison.OrdinalIgnoreCase))
        {
            var startupFramework = ProjectTargetFrameworkResolver.ResolveBuildFramework(
                startupProject.FullName,
                frameworkOrNull);

            startupOutput = GetIsolatedBuildOutput(sessionDirectory, startupProject.FullName, startupFramework);
            RunDotnetBuild(startupProject.FullName, startupFramework, startupOutput);
        }

        return WorkspaceBuildResult.RequirePrimaryAssembly(
            sessionDirectory,
            projectFile,
            startupProject,
            projectFramework,
            projectOutput,
            startupOutput);
    }

    internal static void RunDotnetBuild(
        string csprojFullPath,
        string targetFrameworkMoniker,
        ProjectBuildOutput? isolatedOutput = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(csprojFullPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("q");

        if (!string.IsNullOrWhiteSpace(targetFrameworkMoniker))
        {
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(ProjectTargetFrameworkResolver.NormalizeMoniker(targetFrameworkMoniker));
        }

        if (isolatedOutput is not null)
        {
            startInfo.ArgumentList.Add($"-p:BaseOutputPath={EnsureTrailingSeparator(isolatedOutput.BaseOutputPath)}");
        }

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

    internal static ProjectBuildOutput GetIsolatedBuildOutput(
        string sessionDirectory,
        string csprojFullPath,
        string targetFrameworkMoniker)
    {
        var normalizedProjectPath = Path.GetFullPath(csprojFullPath);
        var projectKey = $"{SessionPaths.GetProjectSessionFolderName(normalizedProjectPath)}-{ShortHash(normalizedProjectPath)}";
        var tfm = ProjectTargetFrameworkResolver.NormalizeMoniker(targetFrameworkMoniker);
        var root = Path.Combine(
            SessionPaths.EnsureSessionDirectory(sessionDirectory),
            ".build",
            projectKey,
            tfm);

        return new ProjectBuildOutput(
            BaseOutputPath: Path.Combine(root, "bin"));
    }

    private static string EnsureTrailingSeparator(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.EndsWith(Path.DirectorySeparatorChar)
            ? fullPath
            : fullPath + Path.DirectorySeparatorChar;
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant()));
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }
}

internal sealed record ProjectBuildOutput(string BaseOutputPath);
