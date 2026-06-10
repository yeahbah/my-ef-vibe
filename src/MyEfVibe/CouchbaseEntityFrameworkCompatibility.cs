namespace MyEfVibe;

/// <summary>
///     Couchbase.EntityFrameworkCore 1.0.x targets EF Core 8 (see Couchbase compatibility guide).
///     EF Core 9+ adds <c>IHistoryRepository.LockReleaseBehavior</c>, which the 1.0.x provider does not implement.
/// </summary>
internal static class CouchbaseEntityFrameworkCompatibility
{
    internal const int SupportedEfCoreMajorVersion = 8;

    internal static bool ReferencesCouchbaseEntityFrameworkCore(string csprojAbsolutePath)
    {
        return CsprojInspector.EnumeratePackageReferenceIds(csprojAbsolutePath)
            .Any(packageId =>
                string.Equals(packageId, "Couchbase.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool TryValidateEfCoreVersion(string efProjectPath, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(efProjectPath)
            || !ReferencesCouchbaseEntityFrameworkCore(efProjectPath))
        {
            return true;
        }

        if (!CsprojInspector.TryReadPackageReferenceVersion(
                efProjectPath,
                "Microsoft.EntityFrameworkCore",
                out var efCoreVersion)
            || efCoreVersion is null)
        {
            return true;
        }

        if (efCoreVersion.Major <= SupportedEfCoreMajorVersion)
        {
            return true;
        }

        error = BuildIncompatibilityMessage(efCoreVersion);

        return false;
    }

    internal static string? TryExplainTypeLoadFailure(string? exceptionMessage)
    {
        if (string.IsNullOrWhiteSpace(exceptionMessage))
        {
            return null;
        }

        if (!exceptionMessage.Contains("LockReleaseBehavior", StringComparison.Ordinal)
            && !exceptionMessage.Contains("CouchbaseHistoryRepository", StringComparison.Ordinal))
        {
            return null;
        }

        return BuildIncompatibilityMessage(efCoreVersion: null);
    }

    private static string BuildIncompatibilityMessage(Version? efCoreVersion)
    {
        var referenced = efCoreVersion is null
            ? "EF Core 9 or later"
            : $"Microsoft.EntityFrameworkCore {efCoreVersion.Major}.{efCoreVersion.Minor}";

        return "Couchbase.EntityFrameworkCore 1.0.x supports EF Core "
               + SupportedEfCoreMajorVersion
               + " only. This project references "
               + referenced
               + ", which adds migration APIs the Couchbase provider does not implement yet."
               + $"{Environment.NewLine}"
               + "Fix: pin EF Core packages on `-p` to 8.0.x (for example `Microsoft.EntityFrameworkCore` Version=\"8.0.14\"),"
               + " target `net8.0`, and reference only `Couchbase.EntityFrameworkCore` as the EF provider."
               + $"{Environment.NewLine}"
               + "See: https://docs.couchbase.com/efcore-provider/current/entity-framework-core-compatibility-guide.html";
    }
}
