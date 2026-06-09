namespace MyEfVibe.Tests;

public sealed class DbInfoReporterCouchbaseTests
{
    [Theory]
    [InlineData("couchbase://localhost", "couchbase://localhost")]
    [InlineData("couchbases://cb.example.com:443", "couchbases://cb.example.com:443")]
    public void RedactCouchbaseConnection_keeps_scheme_and_server(string input, string expected)
    {
        Assert.Equal(expected, DbInfoReporter.RedactCouchbaseConnection(input));
    }

    [Fact]
    public void FormatProviderDisplay_maps_couchbase()
    {
        Assert.Equal("Couchbase", DbInfoReporter.FormatProviderDisplay("Couchbase.EntityFrameworkCore"));
    }
}
