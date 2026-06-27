namespace MyEfVibe;

internal static class ScriptSessionConfigurationFactory
{
    internal static ScriptSessionConfiguration FromCliOptions(
        IEnumerable<string>? scriptSearchPath,
        IEnumerable<string>? scriptLoad,
        IEnumerable<string>? scriptUsing,
        string? resolvedSearchDirectory = null)
    {
        var searchPaths = new List<string>();

        if (scriptSearchPath is not null)
        {
            searchPaths.AddRange(scriptSearchPath);
        }

        if (!string.IsNullOrWhiteSpace(resolvedSearchDirectory))
        {
            searchPaths.Add(resolvedSearchDirectory);
        }

        return ScriptSessionConfiguration.FromCli(
            searchPaths,
            scriptLoad,
            scriptUsing,
            resolvedSearchDirectory);
    }
}
