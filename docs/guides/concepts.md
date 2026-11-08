# Concepts & stack — "what, why, when"

A cheat sheet for terms that come up in the project and in coursework.

---

## CORS — what it's even about

It's **not an API feature** — it's a **browser** security mechanism.
Same-origin policy: a page on origin A cannot read responses from
origin B unless B explicitly allowed it with
`Access-Control-Allow-*` headers.

The key thing almost nobody says out loud:

- **the browser enforces it, not the server.** `curl`, Postman,
  backend-to-backend — CORS doesn't exist for them. The request always
  goes out; the browser just hides the *response* from your JS;
- preflight — an `OPTIONS` request before "non-simple" requests
  (custom headers, non-GET/POST, etc.).

### When it's actually needed

- A browser app calls an API on a **different origin** directly
  (`api.foo.com` vs `foo.com`; `localhost:5173` → `localhost:5269`).
- A public API meant for other people's frontends.

### Why we don't have it

- In dev: the vite proxy — the browser talks to `:5173` (same origin),
  vite proxies to `:5269` server-side. Server→server = CORS doesn't
  apply at all.
- In prod: the dashboard can be served by the same host — same origin
  again.
- Adding `UseCors` now = dead config "for growth". When a real
  cross-origin client shows up we'll add it with a concrete origin
  list (not `AllowAnyOrigin` + credentials — that's a hole).

### Why coursework projects "do it"

Template projects slap `AllowAnyOrigin` on just in case — harmless
there because the API is called straight from a browser on another
port. Legitimate for learning, but the prod rule is different:
a minimal origin list.

---

## CQRS — what it is

**Command Query Responsibility Segregation**: separate models
(sometimes separate stores) for writes and reads. Commands mutate
state; queries read read-optimized projections.

In .NET it usually arrives with MediatR:
`CreateEventCommand → Handler`, `GetDlqQuery → Handler`.

### When it's justified

- Write and read shapes are **genuinely different** (denormalized
  read models, event sourcing).
- Asymmetric load — reads thousands of times more frequent than writes.
- Commands with heavy validation and invariants.

### When it's overkill (our case)

- The domain is CRUD-like: `NotificationEvent` is read and written in
  the same shape. Command/Query handlers would double the code for
  zero benefit.
- `/stats` reads the same table through a 3s StatsCache snapshot —
  honest and cheap.
- CQRS is about **read/write asymmetry**, not "good architecture".
  No asymmetry → no CQRS.

### Confusions worth untangling

- CQRS ≠ microservices, ≠ event sourcing (often together, never
  required), ≠ MediatR as controller decoration.
- "Create/Get methods in one service" — that's naming, not CQRS.

---

## HMAC webhook signing — why and how

When we POST to a receiver, TLS protects the channel but doesn't
answer "did the engine actually send this?". Anyone who knows the
endpoint URL could fire forged webhooks at it.

The industry answer — **HMAC** (Hash-based Message Authentication
Code):

- we and the receiver share a `Secret`;
- we compute `HMAC-SHA256(secret, body)` and put it in the
  `X-Webhook-Signature: sha256=<hex>` header;
- the receiver computes the same and compares. Match → the body is
  unaltered and the sender knew the secret.

Properties that matter:

- **the body is signed, not the headers** — retrying the same payload
  produces an identical signature (dedup-friendly);
- compare in constant time (`CryptographicOperations.FixedTimeEquals`)
  or you're open to a timing attack;
- the secret is per-webhook, stored in `webhook_endpoints.Secret` —
  leaking one doesn't compromise the rest;
- this is exactly GitHub's `X-Hub-Signature-256` — a proven pattern.

What the signature does **not** do: it doesn't encrypt (TLS does that)
and doesn't stop replay without a timestamp inside the signed data.
At our scale that's enough; we'll add a timestamp if we go public.

---

## Local run — how to feed the system

```
dotnet run --project tools/MockReceiver        # receiver :5005
dotnet run --project tools/LoadGen -- --webhook <id> --rps 50 --seconds 60
```

`MockReceiver` — a fake endpoint with a chaos switch:
`/hook` → 200 · `/hook?mode=500` → always 500 · `?mode=429` ·
`?mode=slow` (15s — triggers the sender's timeout) · `?mode=flap`
(alternating 500/200) · `?mode=drop`. `GET /received` shows what
arrived, including the signature header — you can eyeball the HMAC.

This is the TASK-007 range: each mode is a ready-made failure
scenario.

---

## The "pull the pattern or not" heuristic

A pattern is a payment for a specific pain, not a decoration.
Three questions:

1. What pain exists **right now**? (not "might come in handy")
2. What does the pattern remove — and how much code/abstraction does
   it add?
3. Is it cheap to introduce later, when the pain becomes real?

`AllowAnyOrigin` just in case, CQRS on CRUD, repositories over EF,
mappers for every sneeze — the classic "for growth" items that turn
into noise.
