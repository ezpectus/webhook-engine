// MockReceiver — приёмник вебхуков для живых прогонов и хаос-тестов.
//   dotnet run --project tools/MockReceiver   → http://localhost:5005
//
// POST /hook          — принять и вернуть 200
// POST /hook?mode=X   — режим ответа:
//   ok      200 всегда (дефолт)
//   500     всегда 500
//   429     всегда 429 (retryable)
//   slow    200 после 15с паузы (таймаут sender'а 10с → retryable)
//   flap    чередует 500/200 — endpoint "флапает"
//   drop    ничего не отвечает (conn-level)
//
// GET  /received      — последние 200 полученных (тело, время, ip)
// GET  /health        — жив ли приёмник

var app = WebApplication.CreateBuilder(args).Build();

var received = new System.Collections.Concurrent.ConcurrentQueue<object>();
var flapCounter = 0;

void Record(object entry)
{
    received.Enqueue(entry);
    while (received.Count > 200) received.TryDequeue(out _);
}

app.MapPost("/hook", async (HttpRequest req) =>
{
    var mode = req.Query["mode"].ToString();
    var body = await new StreamReader(req.Body).ReadToEndAsync();
    var sig = req.Headers["X-Webhook-Signature"].ToString();
    Record(new { at = DateTimeOffset.UtcNow, mode = mode == "" ? "ok" : mode, signature = sig == "" ? null : sig, body });

    return mode switch
    {
        "500" => Results.StatusCode(500),
        "429" => Results.StatusCode(429),
        "slow" => await Task.Delay(TimeSpan.FromSeconds(15))
                        .ContinueWith(_ => Results.Ok()),
        "flap" => Interlocked.Increment(ref flapCounter) % 2 == 0
                        ? Results.Ok() : Results.StatusCode(500),
        "drop" => await Task.Delay(TimeSpan.FromMinutes(10))
                        .ContinueWith(_ => Results.Ok()),
        _ => Results.Ok(new { received = true })
    };
});

app.MapGet("/received", () => Results.Ok(received.Reverse()));
app.MapDelete("/received", () => { while (received.TryDequeue(out _)) { } return Results.NoContent(); });
app.MapGet("/health", () => Results.Ok(new { status = "ok", received = received.Count }));

app.Run(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://+:5005");
