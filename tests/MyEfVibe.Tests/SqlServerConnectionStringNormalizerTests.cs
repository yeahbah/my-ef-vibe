namespace MyEfVibe.Tests;

public sealed class SqlServerConnectionStringNormalizerTests
{
    [Fact]
    public void Normalize_adds_encrypt_and_trust_flags_for_localhost_without_tls_settings()
    {
        const string connectionString =
            "Server=localhost,1433;Database=AdventureWorks;User Id=sa;Password=secret";

        var normalized = SqlServerConnectionStringNormalizer.Normalize(connectionString);

        Assert.Contains("Encrypt=False", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TrustServerCertificate=True", normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_leaves_existing_tls_settings_unchanged()
    {
        const string connectionString =
            "Server=localhost,1433;Database=AdventureWorks;User Id=sa;Password=secret;Encrypt=false;TrustServerCertificate=true";

        var normalized = SqlServerConnectionStringNormalizer.Normalize(connectionString);

        Assert.Equal(connectionString, normalized);
    }

    [Fact]
    public void Normalize_does_not_change_remote_sql_server_connection_strings()
    {
        const string connectionString =
            "Server=sql.example.com,1433;Database=AdventureWorks;User Id=sa;Password=secret";

        var normalized = SqlServerConnectionStringNormalizer.Normalize(connectionString);

        Assert.Equal(connectionString, normalized);
    }

    [Fact]
    public void Normalize_strips_trusted_connection_when_sql_credentials_are_present_on_linux()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const string connectionString =
            "Server=localhost,1433;Database=AdventureWorks;User Id=sa;Password=secret;Trusted_Connection=True";

        var normalized = SqlServerConnectionStringNormalizer.Normalize(connectionString);

        Assert.DoesNotContain("Trusted_Connection", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("User Id=sa", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Encrypt=False", normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_rejects_integrated_security_without_sql_credentials_on_linux()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const string connectionString =
            "Server=localhost;Database=AdventureWorks_Testing_Placeholder;Trusted_Connection=True";

        var failure = Assert.Throws<InvalidOperationException>(() =>
            SqlServerConnectionStringNormalizer.Normalize(connectionString));

        Assert.Contains("Trusted_Connection", failure.Message, StringComparison.Ordinal);
        Assert.Contains("user-secrets", failure.Message, StringComparison.OrdinalIgnoreCase);
    }
}
