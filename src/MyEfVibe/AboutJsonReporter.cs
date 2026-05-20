using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyEfVibe;

internal static class AboutJsonReporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    internal static void Write(
        object dbContext,
        WorkspaceHost host,
        string workspaceRoot,
        MyEfVibeProvider? provider)
    {
        var payload = Build(dbContext, host, workspaceRoot, provider);
        Console.WriteLine(JsonSerializer.Serialize(payload, SerializerOptions));
    }

    internal static AboutJsonPayload Build(
        object dbContext,
        WorkspaceHost host,
        string workspaceRoot,
        MyEfVibeProvider? provider)
    {
        var contextType = dbContext.GetType();
        var database = contextType.GetProperty("Database")?.GetValue(dbContext);

        string? providerName = null;
        string? connectionState = null;

        if (database is not null)
        {
            providerName = database.GetType().GetProperty("ProviderName")?.GetValue(database) as string;

            if (RelationalDatabaseFacadeInvoker.TryGetDbConnection(
                    database,
                    host.EnumerateLoadedAssemblies(),
                    out var connection)
                && connection is not null)
            {
                connectionState = connection.State.ToString();
            }
        }

        return new AboutJsonPayload
        {
            ToolVersion = ToolInfo.GetVersion(),
            WorkspaceRoot = workspaceRoot,
            SessionDirectory = host.SessionDirectory,
            ProjectPath = host.ProjectPath,
            StartupProjectPath = host.StartupProjectPath,
            DbContext = contextType.Name,
            DbContextFullName = contextType.FullName,
            EfCoreVersion = TryGetEfCoreVersion(),
            Provider = provider?.ToString(),
            ProviderName = providerName,
            ConnectionState = connectionState,
            Runtime = GetRuntimeDescription(),
        };
    }

    private static string GetRuntimeDescription()
    {
        var framework = RuntimeInformation.FrameworkDescription;
        var rid = RuntimeInformation.RuntimeIdentifier;

        return string.IsNullOrWhiteSpace(rid) ? framework : $"{framework} ({rid})";
    }

    private static string? TryGetEfCoreVersion()
    {
        var efAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, "Microsoft.EntityFrameworkCore", StringComparison.Ordinal));

        return efAssembly?.GetName().Version?.ToString(3);
    }

    internal sealed class AboutJsonPayload
    {
        public string ToolVersion { get; init; } = string.Empty;

        public string WorkspaceRoot { get; init; } = string.Empty;

        public string SessionDirectory { get; init; } = string.Empty;

        public string ProjectPath { get; init; } = string.Empty;

        public string StartupProjectPath { get; init; } = string.Empty;

        public string DbContext { get; init; } = string.Empty;

        public string? DbContextFullName { get; init; }

        public string? EfCoreVersion { get; init; }

        public string? Provider { get; init; }

        public string? ProviderName { get; init; }

        public string? ConnectionState { get; init; }

        public string? Runtime { get; init; }
    }
}
