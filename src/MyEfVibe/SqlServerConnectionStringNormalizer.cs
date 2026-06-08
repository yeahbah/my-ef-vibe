namespace MyEfVibe;

/// <summary>
///     Microsoft.Data.SqlClient 4+ defaults to <c>Encrypt=true</c>. Local SQL Server in Docker on Linux/macOS
///     uses a self-signed certificate, which fails the pre-login handshake unless trust/encryption is configured.
///     Windows integrated security (<c>Trusted_Connection</c> / <c>Integrated Security</c>) is not supported on
///     Linux or macOS and surfaces as "Cannot generate SSPI context".
/// </summary>
internal static class SqlServerConnectionStringNormalizer
{
    internal static string Normalize(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)
            || !LooksLikeSqlServerConnection(connectionString))
        {
            return connectionString;
        }

        if (!OperatingSystem.IsWindows())
        {
            connectionString = RejectOrStripIntegratedSecurity(connectionString);
        }

        if (!LooksLikeLocalServer(connectionString))
        {
            return connectionString;
        }

        var builder = new List<string> { connectionString.TrimEnd(';', ' ') };

        if (!ContainsKey(connectionString, "Encrypt"))
        {
            builder.Add("Encrypt=False");
        }

        if (!ContainsKey(connectionString, "TrustServerCertificate"))
        {
            builder.Add("TrustServerCertificate=True");
        }

        return builder.Count == 1
            ? connectionString
            : string.Join(';', builder);
    }

    internal static bool LooksLikeSqlServerConnection(string connectionString)
    {
        return connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase)
               || connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase)
               || connectionString.Contains("Address=", StringComparison.OrdinalIgnoreCase);
    }

    private static string RejectOrStripIntegratedSecurity(string connectionString)
    {
        if (!UsesIntegratedSecurity(connectionString))
        {
            return connectionString;
        }

        if (HasSqlCredentials(connectionString))
        {
            return RemoveIntegratedSecuritySegments(connectionString);
        }

        throw new InvalidOperationException(
            "SQL Server connection strings with `Trusted_Connection` or `Integrated Security` (Windows authentication) "
            + "cannot be used on Linux or macOS."
            + $"{Environment.NewLine}"
            + "Configure SQL authentication on the startup project (`-s`), for example:"
            + $"{Environment.NewLine}"
            + "  dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" "
            + "\"Server=localhost,1433;Database=AdventureWorks;User Id=sa;Password=YOUR_PASSWORD;Encrypt=false;TrustServerCertificate=true\" "
            + "--project ./AdventureWorks.API/AdventureWorks.API.csproj"
            + $"{Environment.NewLine}"
            + "If `ASPNETCORE_ENVIRONMENT=Testing`, user secrets still override `appsettings.Testing.json` when present."
            + " Without user secrets, efvibe reads `Trusted_Connection=True` from Testing settings and SSPI fails.");
    }

    private static bool UsesIntegratedSecurity(string connectionString)
    {
        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf('=');

            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = segment[..separatorIndex].Trim();
            var value = segment[(separatorIndex + 1)..].Trim().Trim('"');

            if (key.Equals("Trusted_Connection", StringComparison.OrdinalIgnoreCase)
                && IsTruthy(value))
            {
                return true;
            }

            if (key.Equals("Integrated Security", StringComparison.OrdinalIgnoreCase)
                && (IsTruthy(value) || value.Equals("SSPI", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSqlCredentials(string connectionString)
    {
        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf('=');

            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = segment[..separatorIndex].Trim();
            var value = segment[(separatorIndex + 1)..].Trim();

            if ((key.Equals("User Id", StringComparison.OrdinalIgnoreCase)
                 || key.Equals("User ID", StringComparison.OrdinalIgnoreCase)
                 || key.Equals("UID", StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrWhiteSpace(value))
            {
                return true;
            }
        }

        return false;
    }

    private static string RemoveIntegratedSecuritySegments(string connectionString)
    {
        var kept = new List<string>();

        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf('=');

            if (separatorIndex <= 0)
            {
                kept.Add(segment);
                continue;
            }

            var key = segment[..separatorIndex].Trim();

            if (key.Equals("Trusted_Connection", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Integrated Security", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            kept.Add(segment);
        }

        return string.Join(';', kept);
    }

    private static bool IsTruthy(string value)
    {
        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || value.Equals("1", StringComparison.Ordinal);
    }

    private static bool LooksLikeLocalServer(string connectionString)
    {
        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf('=');

            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = segment[..separatorIndex].Trim();
            var value = segment[(separatorIndex + 1)..].Trim().Trim('"');

            if (!IsServerKey(key))
            {
                continue;
            }

            var host = value;

            var commaIndex = host.IndexOf(',', StringComparison.Ordinal);

            if (commaIndex >= 0)
            {
                host = host[..commaIndex];
            }

            var backslashIndex = host.IndexOf('\\', StringComparison.Ordinal);

            if (backslashIndex >= 0)
            {
                host = host[..backslashIndex];
            }

            if (IsLocalHost(host))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsServerKey(string key)
    {
        return key.Equals("Server", StringComparison.OrdinalIgnoreCase)
               || key.Equals("Data Source", StringComparison.OrdinalIgnoreCase)
               || key.Equals("Address", StringComparison.OrdinalIgnoreCase)
               || key.Equals("Addr", StringComparison.OrdinalIgnoreCase)
               || key.Equals("Network Address", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLocalHost(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
               || host.Equals("(local)", StringComparison.OrdinalIgnoreCase)
               || host.Equals(".", StringComparison.Ordinal)
               || host.Equals("(localdb)\\mssqllocaldb", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsKey(string connectionString, string key)
    {
        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf('=');

            if (separatorIndex <= 0)
            {
                continue;
            }

            if (segment[..separatorIndex].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
