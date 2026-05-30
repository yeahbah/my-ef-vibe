using System.Reflection;
using System.Runtime.InteropServices;

namespace MyEfVibe;

internal static class AppMetadata
{
    internal const string CommandName = "efvibe";
    internal const string ProductName = "MyEfVibe";
    internal const string License = "Apache-2.0";
    internal const string WebsiteUrl = "https://myefvibe.com";
    internal const string RepositoryUrl = "https://github.com/yeahbah/my-ef-vibe";
    internal const string NuGetUrl = "https://www.nuget.org/packages/efvibe";

    private static readonly Assembly Assembly = typeof(AppMetadata).Assembly;

    internal static string GetDescription()
    {
        return Assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description
               ?? "Interactive EF Core LINQ REPL for external projects.";
    }

    internal static string GetAuthor()
    {
        return Assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company
               ?? "Arnold Diaz";
    }

    internal static string GetRuntimeDescription()
    {
        var framework = RuntimeInformation.FrameworkDescription;
        var rid = RuntimeInformation.RuntimeIdentifier;

        return string.IsNullOrWhiteSpace(rid) ? framework : $"{framework} ({rid})";
    }
}