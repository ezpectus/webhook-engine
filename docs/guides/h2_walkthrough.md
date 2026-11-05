# H-2 walkthrough — client `Idempotency-Key` header

Hands-on task. This doc lays out the path; the implementation is
yours. Ticket: `docs/tasks.md` → H-2. **Depends on H-1** — it builds
on the value-in-Redis marker (`<guid>` → `<guid>|<bodyHash>`) and the
`GetEventIdAsync` shape.

## What you're building

Today dedup derives the key from the body: `sha256(wh + payload)`.
Two different events with identical bodies collide; a client that
already has its own event ids can't use them. Stripe-style fix: the
client sends `Idempotency-Key: <their-key>` and owns idempotency.

```
header present → key material = header value (body NOT in the key)
header absent  → key material = payload   (today's behaviour)
```

Consequence of "body not in the key": `key=K body=A` then
`key=K body=B` must NOT silently dedup — B would be lost while the
client believes it was accepted. Detect it → `409`.

## Where the code lives

- `src/WebhookEngine.Api/Endpoints/Endpoints.cs` → `IngestEvent`
  (signature, key material choice, marker compare, 409 path)
- `Core/Abstractions/IDeduplicationStore.cs` → the marker value now
  carries two fields → one shape decision below
- `Infrastructure/Redis/RedisDeduplicationStore.cs` → value format

## Decisions — pick per line, defaults recommended

1. **PG key for the header path:** `sha256(wh + header + bucket)` —
   same bucket as content keys. Recommended: one dedup horizon
   (`WindowHours`) for both paths, semantics don't fork.
   Alternative: no bucket → client keys dedup forever (PG index
   never forgets). Valid, but two semantics to reason about.
2. **Same key + different body → `409 Conflict`**
   `{ error = "idempotency_key_reused" }`. Recommended (canonical
   REST conflict; Stripe uses 422 — either is defensible, journal it).
3. **Where the first body hash lives:** inside the Redis marker
   value → `"<eventId>|<sha256(payload)>"`. On the 23505-catch path
   no hash needed — `seen.Payload` is right there, compare strings.
4. **Marker read shape:** `GetEventIdAsync` →
   `GetMarkerAsync(key) → string?`; the endpoint splits on the
   first `|`. The store stays dumb (no parsing inside Infra).
   Alternative: return a small record — fine too, more ceremony.
5. **Header validation:** absent/whitespace → content path (treat
   as not sent). `> 256` chars → `400`. No charset rules — it's
   hashed anyway.
6. **Body hash when header path:** `ComputeKey(wh, payload)` IS a
   sha256 of content — reuse it as `bodyHash` (no new helper).
7. **Marker format — uniform:** always write `<id>|<bodyHash>`,
   even on the content path (where the hash is redundant — it
   equals the key material, but one format beats two).
   Header-path values are only ever written by header-path code →
   the `|` is always there when you read a header-keyed marker.

## Write map — every edit, in order

**`IngestEvent` signature:** add
`[FromHeader(Name = "Idempotency-Key")] string? idemKey`
(`using Microsoft.AspNetCore.Mvc`). Declarative, shows in Swagger.

**Top of handler, after webhook validation:**
1. `if (idemKey is { Length: > 256 }) → 400`.
2. `var material = string.IsNullOrWhiteSpace(idemKey) ? req.Payload : idemKey;`
   then `redisKey = ComputeKey(wh, material)`,
   `key = ComputeKey(wh, material, bucket)` — the H-1 machinery
   unchanged, only the input swaps.
3. `bodyHash` = `ComputeKey(wh, req.Payload)` — computed once,
   needed only when `material != req.Payload` (header path) for
   compare + marker write.

**Dedup check (header path adds one branch):**
- `GetMarkerAsync(redisKey)` → `null` → miss, continue.
- hit → split `id|hash`:
  - header path && `hash != bodyHash` → `409` (**not** DedupHit —
    it's a rejection, not a dup).
  - else → DedupHit + `{ eventId = id, "duplicate" }`.

**23505 catch (header path):** `seen.Payload != req.Payload` → 409;
else `Duplicate(seen)`. Content path: impossible to mismatch
(payload is in the key) — no compare needed, keep `Duplicate(seen)`.

**Mark:** `MarkAsync(redisKey, $"{ev.Id}|{bodyHash}", window)` —
uniform format. (`bodyHash` is already `ComputeKey(wh, payload)` —
on the content path that's the same string as `redisKey`; storing
it anyway keeps one format.)

**`IDeduplicationStore`:** `GetEventIdAsync` →
`Task<string?> GetMarkerAsync(string key, CancellationToken ct)`.
`MarkAsync` signature can stay `(key, string value, window)` —
take `string` instead of `Guid` and let the caller format. Or
`(key, eventId, bodyHash, window)` — your call; the dumb-string
version keeps Infra parsing-free.

**`RedisDeduplicationStore`:** `StringGet` → return raw string;
`StringSet` the formatted value. No parsing inside.

## Edge cases to think about

- **`key=K, body=A` accepted; retry `key=K, body=A'` mid-edit** —
  the 409 tells the client "your key collided", not "dup".
- **Header-keyed marker survives past its window?** TTL is the
  same `window` — consistent with decision 1.
- **Client sends header on retry only** (first POST without) —
  two different key materials → two events. That's on the client;
  worth a sentence in the journal (it's the standard contract).
- **Whitespace header `" "`** → treated as absent (decision 5) —
  say it out loud when you write it.
- **409 vs swallowing:** what does the client's retry-loop do on
  409? At least it *knows* — silent dup hides data loss.

## Tests / verification

Endpoint-level again — manual e2e is the honest check (same
infra gap as H-1). With `Dedup__WindowHours` normal:

```
POST /events {webhookId, payload:"A"}                    → 202 id₁
POST /events + Idempotency-Key: K {payload:"A"}          → 202 id₂  (different key space)
POST /events + Idempotency-Key: K {payload:"A"}          → 200 duplicate id₂
POST /events + Idempotency-Key: K {payload:"B"}          → 409
POST /events + Idempotency-Key: L {payload:"A"}          → 202 id₃
POST /events + Idempotency-Key: <257 chars>              → 400
```

## Definition of done

- [ ] AC: same header + same body → duplicate w/ original id;
      same header + different body → 409; different headers →
      separate events
- [ ] The 409-vs-dup distinction implemented in *both* dedup
      paths (Redis hit and PG catch)
- [ ] Journal line in `PLAN.md`: PG-key bucketing choice, 409 vs
      422, who owns idempotency when
- [ ] Voice-checkpoint: why can't the body-mismatch happen on the
      content path? What does the client learn from 409 that
      `duplicate` would hide?
