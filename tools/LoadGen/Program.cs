using System.Diagnostics;
using System.Text;
using System.Text.Json;

// Load generator for /events.
//   dotnet run --project tools/LoadGen -- --webhook <guid> [options]
// Options: --url (def http://localhost:5269) · --rps (def 50)
//          --seconds (def 30) · --burst (x5 per tick) · --dup (dup ratio, def 0)
// 202 = accepted, 200 = duplicate — counters show dedup live.

var url = GetArg("--url", "http://localhost:5269")!;
var webhook = GetArg("--webhook", null)
    ?? throw new ArgumentException("--webhook <guid> required");
var rps = int.Parse(GetArg("--rps", "50")!);
var seconds = int.Parse(GetArg("--seconds", "30")!);
var burst = args.Contains("--burst");
var dupRate = double.Parse(GetArg("--dup", "0")!, System.Globalization.CultureInfo.InvariantCulture);

using var http = new HttpClient();
var rng = new Random();
var payloads = new List<string>();
long sent = 0, accepted = 0, dup = 0, err = 0;

Console.WriteLine($"loadgen → {url} | webhook {webhook} | rps {rps}{(burst ? " ×5" : "")} | {seconds}s | dup {dupRate:P0}");

var timer = Stopwatch.StartNew();
while (timer.Elapsed < TimeSpan.FromSeconds(seconds))
{
    var tick = Stopwatch.StartNew();
    var batch = burst ? rps * 5 : rps;
    await Task.WhenAll(Enumerable.Range(0, batch).Select(_ => SendOneAsync()));

    Console.WriteLine($"{timer.Elapsed.TotalSeconds,4:F0}s  sent={sent,6} accepted={accepted,6} dup={dup,5} err={err,4}");
    var rest = TimeSpan.FromSeconds(1) - tick.Elapsed;
    if (rest > TimeSpan.Zero) await Task.Delay(rest);
}

Console.WriteLine($"done: sent={sent} accepted={accepted} dup={dup} err={err}");

async Task SendOneAsync()
{
    try
    {
        var body = JsonSerializer.Serialize(new { webhookId = webhook, payload = MakePayload() });
        var resp = await http.PostAsync($"{url}/events",
            new StringContent(body, Encoding.UTF8, "application/json"));
        Interlocked.Increment(ref sent);
        switch ((int)resp.StatusCode)
        {
            case 202: Interlocked.Increment(ref accepted); break;
            case 200: Interlocked.Increment(ref dup); break;
            default: Interlocked.Increment(ref err); break;
        }
    }
    catch { Interlocked.Increment(ref err); }
}

string MakePayload()
{
    if (payloads.Count > 0 && rng.NextDouble() < dupRate)
        return payloads[rng.Next(payloads.Count)];
    var p = $"{{\"i\":{rng.Next(0, 1_000_000)}}}";
    payloads.Add(p);
    return p;
}

string? GetArg(string name, string? fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}
