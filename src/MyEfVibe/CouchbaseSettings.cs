namespace MyEfVibe;

internal sealed class CouchbaseSettings
{
    internal const string SettingsRootName = "Couchbase";

    internal const string LegacySettingsRootName = "DefaultConnection";

    internal string ConnectionString { get; init; } = string.Empty;

    internal string Username { get; init; } = string.Empty;

    internal string Password { get; init; } = string.Empty;

    internal string BucketName { get; init; } = string.Empty;

    internal string ScopeName { get; init; } = string.Empty;

    internal string? CollectionName { get; init; }

    internal bool IsComplete =>
        !string.IsNullOrWhiteSpace(ConnectionString)
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Password)
        && !string.IsNullOrWhiteSpace(BucketName)
        && !string.IsNullOrWhiteSpace(ScopeName);
}
