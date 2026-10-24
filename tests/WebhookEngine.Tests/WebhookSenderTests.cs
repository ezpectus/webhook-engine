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

    [Fact]
    public async Task Send_WithSecret_AddsHmacSignature()
    {
        string? signature = null;
        var sender = Sender(new FakeHandler(req =>
        {
            signature = req.Content?.Headers.TryGetValues("X-Webhook-Signature", out var v) == true
                ? v.First() : null;
            return Reply(HttpStatusCode.OK);
        }));

        var r = await sender.SendAsync("https://example.com/hook", "{\"a\":1}", CancellationToken.None, "s3cret");

        Assert.True(r.Success);
        var expected = Convert.ToHexString(
            System.Security.Cryptography.HMACSHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("s3cret"),
                System.Text.Encoding.UTF8.GetBytes("{\"a\":1}"))).ToLowerInvariant();
        Assert.Equal($"sha256={expected}", signature);
    }

    [Fact]
    public async Task Send_WithoutSecret_NoSignatureHeader()
    {
        var sender = Sender(new FakeHandler(req =>
        {
            Assert.False(req.Content?.Headers.Contains("X-Webhook-Signature") ?? true);
            return Reply(HttpStatusCode.OK);
        }));

        var r = await sender.SendAsync("https://example.com/hook", "{}", CancellationToken.None);
        Assert.True(r.Success);
    }
}
