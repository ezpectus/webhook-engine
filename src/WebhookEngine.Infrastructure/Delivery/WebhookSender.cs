using System.Security.Cryptography;
using System.Text;

namespace WebhookEngine.Infrastructure.Delivery;

public sealed record SendResult(bool Success, int? StatusCode, string? Error, bool IsRetryable)
{
    public static SendResult Ok(int code) => new(true, code, null, false);
    public static SendResult Failed(int? code, string error, bool retryable) => new(false, code, error, retryable);
    public static SendResult NonRetryable(string error) => new(false, null, error, false);
}

public sealed class WebhookSender(HttpClient http)
{
    public async Task<SendResult> SendAsync(
        string url, string payload, CancellationToken ct, string? secret = null)
    {
        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");

            // D14: sign the body with HMAC-SHA256 — the receiver verifies the
            // webhook is ours and unaltered (like GitHub's X-Hub-Signature-256).
            if (!string.IsNullOrEmpty(secret))
            {
                var key = Encoding.UTF8.GetBytes(secret);
                var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
                content.Headers.Add("X-Webhook-Signature", $"sha256={Convert.ToHexString(mac).ToLowerInvariant()}");
            }

            // Review fix: the response must be disposed. An undisposed
            // HttpResponseMessage holds its pool connection until GC —
            // at high RPS the pool exhausts and new requests wait on sockets.
            // Was: var resp = await http.PostAsync(url, content, ct);
            //   — fine at 100 rps, dies at 1000+.
            using var resp = await http.PostAsync(url, content, ct);
            var code = (int)resp.StatusCode;

            if (resp.IsSuccessStatusCode)
                return SendResult.Ok(code);

            // Retry only what can change: 5xx and 429 (receiver rate limit).
            var retryable = code == 429 || code >= 500;
            return SendResult.Failed(code, $"HTTP {code}", retryable);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return SendResult.Failed(null, "timeout", true);
        }
        catch (HttpRequestException ex)
        {
            return SendResult.Failed(null, ex.Message, true);
        }
    }
}
