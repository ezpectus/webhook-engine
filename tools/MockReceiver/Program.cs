// MockReceiver — webhook sink for live runs and chaos tests.
//   dotnet run --project tools/MockReceiver   → http://localhost:5005
//
// POST /hook          — accept and return 200
// POST /hook?mode=X   — response mode:
//   ok      always 200 (default)
//   500     always 500
//   429     always 429 (retryable)
//   slow    200 after a 15s pause (sender timeout is 10s → retryable)
//   flap    alternates 500/200 — a "flapping" endpoint
//   drop    never responds (conn-level)
//
// GET  /received      — last 200 received (body, time, ip)
// GET  /health        — is the sink alive

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
