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

    internal static void Write()
    {
        Console.WriteLine(JsonSerializer.Serialize(Build(), SerializerOptions));
    }

    internal static AboutJsonPayload Build() =>
        new()
        {
            ToolVersion = ToolInfo.GetVersion(),
            Command = AppMetadata.CommandName,
            ProductName = AppMetadata.ProductName,
            Description = AppMetadata.GetDescription(),
            Author = AppMetadata.GetAuthor(),
            License = AppMetadata.License,
            Website = AppMetadata.WebsiteUrl,
            Repository = AppMetadata.RepositoryUrl,
            NuGet = AppMetadata.NuGetUrl,
            Runtime = AppMetadata.GetRuntimeDescription(),
        };

    internal sealed class AboutJsonPayload
    {
        public string ToolVersion { get; init; } = string.Empty;

        public string Command { get; init; } = string.Empty;

        public string ProductName { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string Author { get; init; } = string.Empty;

        public string License { get; init; } = string.Empty;

        public string Website { get; init; } = string.Empty;

        public string Repository { get; init; } = string.Empty;

        public string NuGet { get; init; } = string.Empty;

        public string Runtime { get; init; } = string.Empty;
    }
}
