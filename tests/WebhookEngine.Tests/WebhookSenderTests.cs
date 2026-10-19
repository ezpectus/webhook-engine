using System.Net;
using WebhookEngine.Infrastructure.Delivery;

namespace WebhookEngine.Tests;

public class WebhookSenderTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    private sealed class ThrowingHandler(Exception ex) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw ex;
    }

    private static WebhookSender Sender(HttpMessageHandler handler)
        => new(new HttpClient(handler));

    private static HttpResponseMessage Reply(HttpStatusCode code)
        => new(code);

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task Send_2xx_IsSuccess(HttpStatusCode code)
    {
        var sender = Sender(new FakeHandler(_ => Reply(code)));
        var r = await sender.SendAsync("https://example.com/hook", "{}", CancellationToken.None);
        Assert.True(r.Success);
        Assert.Equal((int)code, r.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Send_RetryableStatuses_MarkedRetryable(HttpStatusCode code)
    {
        var sender = Sender(new FakeHandler(_ => Reply(code)));
        var r = await sender.SendAsync("https://example.com/hook", "{}", CancellationToken.None);
        Assert.False(r.Success);
        Assert.True(r.IsRetryable);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Send_4xx_NotRetryable(HttpStatusCode code)
    {
        var sender = Sender(new FakeHandler(_ => Reply(code)));
        var r = await sender.SendAsync("https://example.com/hook", "{}", CancellationToken.None);
        Assert.False(r.Success);
        Assert.False(r.IsRetryable);
    }

    [Fact]
    public async Task Send_ConnectionFailure_IsRetryable()
    {
        var sender = Sender(new ThrowingHandler(new HttpRequestException("conn refused")));
        var r = await sender.SendAsync("https://example.com/hook", "{}", CancellationToken.None);
        Assert.False(r.Success);
        Assert.True(r.IsRetryable);
        Assert.Null(r.StatusCode);
    }
}
