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
    public void TryParseRequest_returns_null_for_invalid_json()
    {
        Assert.Null(ServeProtocol.TryParseRequest("not json"));
    }
}
