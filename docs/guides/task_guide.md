# Task guide — concepts behind the hands-on tickets

For tired-mode reading. No code here — just the understanding you'll
need when you sit down to write it yourself.

---

## TASK-004 · Token bucket

### Analogy

A bucket that tokens drip into at a constant rate. The bucket holds at
most `capacity` tokens — the extra ones just spill over and are lost.
Every webhook send costs **1 token**:

- token present → take it, send right away;
- no tokens → wait for the next one to drip in.

### Why TWO parameters, not just "RPS"

- `refillPerSecond` — the sustained average limit (what the API
  guarantees long-term: "no more than 10/sec").
- `capacity` — the allowed burst. Example: limit 10/sec, capacity 30.
  A client quiet for 3 seconds accumulates 30 → can fire 30 requests
  at once, then back to 10/sec. Without capacity even an honest client
  could never make 11 requests in a row, in any world.

That's why in this project `capacity` is usually `RateLimitPerSecond`
(one second of burst) or k×RPS — your call, record it.

### The key implementation idea — lazy refill

No "dripping" timer needed. On every bucket access:

```
tokens = min(capacity, tokens + elapsedSeconds * refillPerSecond)
```

where elapsed = time since the last access. One field `_lastRefill`,
one formula. That's it.

### Time comes from outside, not inside

The class must not call `DateTimeOffset.UtcNow` itself. Take a
`TimeProvider` in the constructor (`TimeProvider.System` by default).
Otherwise a "wait 200ms" test waits real 200ms — tests should fly and
never flake. .NET 8: `TimeProvider` is in the BCL; for tests —
`FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`).

### Token bucket vs leaky bucket

- **Bucket:** accumulate — release in a batch. Allows bursts up to
  capacity.
- **Leaky:** outbound rate strictly fixed, no bursts at all, the
  queue gets smoothed.

For external APIs bucket is the norm: their limits count "N per
window", and a burst inside the window is harmless. Leaky is needed
where the mere frequency of requests is harmful.

### Pitfalls (I'll check at review)

- several workers hitting one bucket at once → `tokens` goes negative
  without synchronization;
- refill without `min(capacity)` → bottomless bucket;
- `refillPerSecond <= 0` → what happens? (think about throwing in
  the constructor);
- `TimeUntilAvailable` at `tokens >= 1` — must be `Zero`, not
  "a little bit".

---

## TASK-005 · Retry scheduler

### The three ideas it's built from

1. **Exp backoff** — `delay = base * 2^attempt` (1s, 2s, 4s, 8s, ...).
   A down endpoint shouldn't be hammered immediately: the pause grows,
   giving it time to stand back up. Without a `cap` 2^n flies into
   hours — hence the 5-minute ceiling (D9).

2. **Jitter** — random spread on the delay. Picture it: 1000 events
   failed simultaneously (the endpoint went down for everyone at the
   same second). Without jitter all 1000 come back for retry at the
   same time too — the second wave kills the endpoint that just got
   up (thundering herd). Jitter smears the crowd across a corridor.
   Options: `rand(delay)` (full) or `delay/2 + rand(delay/2)` (equal).

3. **Min-heap by due-time** — `PriorityQueue<Guid, DateTimeOffset>`:
   the soonest retry sits on top, extraction is O(log n).

### The single most important thing to understand

**The heap is an accelerator; PG is the truth.** The in-memory heap
disappears on restart — and nothing bad happens: `NextAttemptAt` is
written in the table, and the sweeper already knows how to scan it.
So the implementation must keep the PG field and the heap in
agreement — and recovery is free, from the table.

### Edge cases before coding

- `PriorityQueue` has no Remove — cancel/reschedule is lazy: pop a
  due entry → compare with the current `NextAttemptAt` in PG →
  mismatch (someone rescheduled/cancelled) → drop the entry.
- max-attempts is checked BEFORE scheduling a retry, not after
  writing to the DLQ.
- Jitter must differ per event — shared `Random.Shared` gives that;
  one identical seed for all does not.

---

## Common to both tickets

- Classes live in `Core` — pure algorithms with zero dependencies.
- You write the tests yourself: a test is your behavioural spec.
- Stuck — ladder: "hint" → "question" → "pseudocode". No code from me
  under the hands-on rule.
