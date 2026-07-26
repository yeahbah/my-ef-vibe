namespace MyEfVibe.Tests;

public sealed class DbInfoReporterConnectionStringRedactionTests
{
    [Theory]
    [InlineData(
        "Server=localhost,1433;Database=AdventureWorks;User Id=sa;Password=s3cret!;Encrypt=false",
        "s3cret!",
        "AdventureWorks")]
    [InlineData(
        "Host=localhost;Database=aw;Username=postgres;Password=hunter2",
        "hunter2",
        "aw")]
    [InlineData(
        "Data Source=app.db;Password=sqlite-secret",
        "sqlite-secret",
        "app.db")]
    [InlineData(
        "Server=tcp:example.database.windows.net;Database=app;User ID=admin;Pwd=top-secret",
        "top-secret",
        "example.database.windows.net")]
    public void RedactRelationalConnectionString_masks_password_keys(
        string input,
        string secretValue,
        string preservedToken)
    {
        var redacted = DbInfoReporter.RedactRelationalConnectionString(input);

        Assert.DoesNotContain(secretValue, redacted, StringComparison.Ordinal);
        Assert.Contains("****", redacted, StringComparison.Ordinal);
        Assert.Contains(preservedToken, redacted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedactRelationalConnectionString_masks_azure_account_key()
    {
        var input =
            "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=abc123/+secret==;EndpointSuffix=core.windows.net";

        var redacted = DbInfoReporter.RedactRelationalConnectionString(input);

        Assert.DoesNotContain("abc123/+secret==", redacted, StringComparison.Ordinal);
        Assert.Contains("AccountKey=****", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AccountName=myaccount", redacted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedactRelationalConnectionString_preserves_passwordless_sqlite()
    {
        var input = "Data Source=/tmp/app.db;Mode=ReadWriteCreate";

        var redacted = DbInfoReporter.RedactRelationalConnectionString(input);

        Assert.DoesNotContain("****", redacted, StringComparison.Ordinal);
        Assert.Contains("/tmp/app.db", redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RedactRelationalConnectionString_empty_returns_placeholder(string? input)
    {
        Assert.Equal("(empty)", DbInfoReporter.RedactRelationalConnectionString(input));
    }

    [Fact]
    public void RedactRelationalConnectionString_fallback_masks_malformed_password()
    {
        // Unbalanced quotes make DbConnectionStringBuilder reject the value.
        var input = "Server=localhost;Password=\"unterminated;Database=app";

        var redacted = DbInfoReporter.RedactRelationalConnectionString(input);

        Assert.DoesNotContain("unterminated", redacted, StringComparison.Ordinal);
        Assert.Contains("Password=****", redacted, StringComparison.OrdinalIgnoreCase);
    }
}
