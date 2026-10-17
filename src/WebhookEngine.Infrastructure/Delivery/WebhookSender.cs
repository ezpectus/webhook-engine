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
    public async Task<SendResult> SendAsync(string url, string payload, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = await http.PostAsync(url, content, ct);
            var code = (int)resp.StatusCode;

            if (resp.IsSuccessStatusCode)
                return SendResult.Ok(code);

            // Ретраим только то, что может измениться: 5xx и 429 (у получателя rate limit).
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
