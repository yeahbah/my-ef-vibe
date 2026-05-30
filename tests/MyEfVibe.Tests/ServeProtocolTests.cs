namespace MyEfVibe.Tests;

public sealed class ServeProtocolTests
{
    [Fact]
    public void TryParseRequest_parses_eval_with_plan()
    {
        var request = ServeProtocol.TryParseRequest(
            """{"type":"eval","expression":"db.Products.Count()","withPlan":true}""");

        Assert.NotNull(request);
        Assert.Equal("eval", request!.Type);
        Assert.Equal("db.Products.Count()", request.Expression);
        Assert.True(request.WithPlan);
    }

    [Fact]
    public void TryParseRequest_parses_scan_with_options()
    {
        var request = ServeProtocol.TryParseRequest(
            """{"type":"scan","mode":"deep","respectDismissals":true,"minSeverity":"warning"}""");

        Assert.NotNull(request);
        Assert.Equal("scan", request!.Type);
        Assert.Equal("deep", request.Mode);
        Assert.True(request.RespectDismissals);
        Assert.Equal("warning", request.MinSeverity);
    }

    [Fact]
    public void TryParseRequest_parses_describe()
    {
        var request = ServeProtocol.TryParseRequest("""{"type":"describe","entity":"Products"}""");

        Assert.NotNull(request);
        Assert.Equal("describe", request!.Type);
        Assert.Equal("Products", request.Entity);
    }

    [Fact]
    public void TryParseRequest_parses_dbinfo_and_tables()
    {
        Assert.Equal("dbinfo", ServeProtocol.TryParseRequest("""{"type":"dbinfo"}""")!.Type);
        Assert.Equal("tables", ServeProtocol.TryParseRequest("""{"type":"tables"}""")!.Type);
    }

    [Fact]
    public void TryParseRequest_parses_completions()
    {
        var request = ServeProtocol.TryParseRequest("""{"type":"completions","prefix":"db.Pro"}""");

        Assert.NotNull(request);
        Assert.Equal("completions", request!.Type);
        Assert.Equal("db.Pro", request.Prefix);
    }

    [Fact]
    public void TryParseRequest_returns_null_for_invalid_json()
    {
        Assert.Null(ServeProtocol.TryParseRequest("not json"));
    }
}