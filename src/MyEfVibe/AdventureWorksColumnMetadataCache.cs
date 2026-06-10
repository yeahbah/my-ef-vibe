using System.Collections.Concurrent;

namespace MyEfVibe;

/// <summary>
///     Thread-safe cache of live column metadata indexes. Each registration gets a stable id
///     embedded in the emitted model customizer so parallel DbContext builds do not race.
/// </summary>
internal static class AdventureWorksColumnMetadataCache
{
    private static long _nextRegistrationId;

    private static readonly ConcurrentDictionary<long, Dictionary<(string Schema, string Table), Dictionary<string, string>>>
        ByRegistrationId = new();

    internal static long Store(
        string connectionString,
        Dictionary<(string Schema, string Table), Dictionary<string, string>> columnIndex)
    {
        if (columnIndex.Count == 0)
        {
            return 0;
        }

        _ = connectionString;
        var registrationId = Interlocked.Increment(ref _nextRegistrationId);
        ByRegistrationId[registrationId] = columnIndex;

        return registrationId;
    }

    internal static Dictionary<(string Schema, string Table), Dictionary<string, string>>? TryGet(long registrationId)
    {
        if (registrationId <= 0)
        {
            return null;
        }

        return ByRegistrationId.TryGetValue(registrationId, out var columnIndex)
            ? columnIndex
            : null;
    }
}
