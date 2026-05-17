namespace MyEfVibe;

internal static class DotNetInstallRoot
{
    internal static string Resolve()
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");

        if (!string.IsNullOrWhiteSpace(dotnetRoot) && Directory.Exists(dotnetRoot))
            return Path.GetFullPath(dotnetRoot);

        var coreLibDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        // {dotnet}/shared/Microsoft.NETCore.App/{version}
        return Path.GetFullPath(Path.Combine(coreLibDirectory, "..", "..", ".."));
    }
}
