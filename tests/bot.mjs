// E2E test bot — drives the whole delivery loop against a live stack.
// Unlike smoke.mjs (API surface), this verifies END-TO-END behavior:
// events actually arrive at the receiver, retries happen, DLQ fills,
// replay delivers.
//
//   docker compose up -d          (all 5 services)
//   node tests/bot.mjs
//
//   API        — engine API for the bot (default http://localhost:8080)
//   HOOK_URL   — URL the *API process* dials. Inside compose it resolves
//                the mockreceiver by name → HOOK_URL=http://mockreceiver:5005
//   RECEIVER   — URL the *bot* uses to inspect/control the receiver
//                (default http://localhost:5005)
//
// Runtime ~2 min (DLQ leg needs the full 1+2+4+8s backoff to exhaust).
// Exit 0 = every scenario passed.

const API = process.env.API ?? 'http://localhost:8080'
const HOOK = process.env.HOOK_URL ?? 'http://localhost:5005'
const RECEIVER = process.env.RECEIVER ?? 'http://localhost:5005'
const tag = `bot-${Date.now()}`

let failures = 0
const ok = (name, cond, extra = '') => {
  console.log(`${cond ? ' ok ' : 'FAIL'}  ${name}${extra ? `  (${extra})` : ''}`)
  if (!cond) failures++
}
const sleep = (ms) => new Promise(r => setTimeout(r, ms))

const get = (path) => fetch(`${API}${path}`)
const post = (path, body, headers = {}) =>
  fetch(`${API}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...headers },
    body: JSON.stringify(body),
  })
const rget = (path) => fetch(`${RECEIVER}${path}`)

const setMode = (m) => fetch(`${RECEIVER}/mode?m=${m}`, { method: 'POST' })

// EventStatus serializes as a NUMBER on this API (no enum converter):
// Pending=0 InFlight=1 RetryScheduled=2 Delivered=3 DeadLettered=4.
const STATUS = { Delivered: 3, DeadLettered: 4 }

// Poll GET /events/{id} until status or timeout. Returns the last seen event.
async function waitStatus(id, want, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  let ev = null
  while (Date.now() < deadline) {
    const r = await get(`/events/${id}`)
    if (r.ok) {
      ev = await r.json()
      if (ev.status === STATUS[want]) return ev
    }
    await sleep(1000)
  }
  return ev
}

async function receiverHas(marker, timeoutMs) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    const items = await (await rget('/received')).json()
    const hit = items.find(i => typeof i.body === 'string' && i.body.includes(marker))
    if (hit) return hit
    await sleep(500)
  }
  return null
}

console.log(`${tag}  api=${API}  hook=${HOOK}  receiver=${RECEIVER}`)

// --- 0. preflight -----------------------------------------------------------
const health = await (await get('/health')).json()
ok('health: api + postgres up', health.status === 'healthy' || health.postgres === true,
  `status=${health.status}`)
const rhealth = await (await rget('/health')).json()
ok('receiver: alive', rhealth.status === 'ok')

// --- 1. happy path: post -> delivered at receiver ----------------------------
await setMode('ok')
// secret -> the sender must attach X-Webhook-Signature (HMAC-SHA256,
// unsigned only when a webhook has no secret — that's by design).
const whG = await (await post('/webhooks', {
  url: `${HOOK}/hook`, rateLimitPerSecond: 50, secret: 'bot-secret' })).json()
ok('register: global-mode webhook', typeof whG.webhookId === 'string')

const m1 = `${tag}-e1`
const e1 = await (await post('/events', {
  webhookId: whG.webhookId, payload: JSON.stringify({ marker: m1 }) })).json()
ok('e1: accepted', e1.status === 'accepted')

const ev1 = await waitStatus(e1.eventId, 'Delivered', 30000)
ok('e1: Delivered', ev1?.status === STATUS.Delivered, `status=${ev1?.status}`)
const seen1 = await receiverHas(m1, 5000)
ok('e1: body arrived at receiver', seen1 !== null)
ok('e1: HMAC signature header present', seen1?.signature != null,
  seen1 ? `sig=${(seen1.signature ?? 'none').slice(0, 16)}` : 'no record')

// --- 2. dedup under concurrency ----------------------------------------------
const m2 = `${tag}-e2`
const dupBody = JSON.stringify({ marker: m2 })
const res = await Promise.all(Array.from({ length: 10 }, () =>
  post('/events', { webhookId: whG.webhookId, payload: dupBody }).then(r => r.json())))
const ids = new Set(res.map(r => r.eventId))
const accepted = res.filter(r => r.status === 'accepted').length
ok('dedup: 10 parallel -> 1 event', ids.size === 1 && accepted === 1,
  `ids=${ids.size} accepted=${accepted}`)

// --- 3. retry: flapping endpoint recovers ------------------------------------
const whF = await (await post('/webhooks', {
  url: `${HOOK}/hook?mode=flap`, rateLimitPerSecond: 50 })).json()
const m3 = `${tag}-e3`
const e3 = await (await post('/events', {
  webhookId: whF.webhookId, payload: JSON.stringify({ marker: m3 }) })).json()
const ev3 = await waitStatus(e3.eventId, 'Delivered', 60000)
ok('flap: eventually Delivered', ev3?.status === STATUS.Delivered,
  `status=${ev3?.status} attempts=${ev3?.attemptCount}`)
ok('flap: took retries', (ev3?.attemptCount ?? 0) >= 2, `attempts=${ev3?.attemptCount}`)

// --- 4. DLQ: dead endpoint exhausts retries ----------------------------------
await setMode('500')
const m4 = `${tag}-e4`
const e4 = await (await post('/events', {
  webhookId: whG.webhookId, payload: JSON.stringify({ marker: m4 }) })).json()
// 5 attempts with 1+2+4+8s backoff + send time + jitter ≈ 20-40s.
const ev4 = await waitStatus(e4.eventId, 'DeadLettered', 120000)
ok('dlq: DeadLettered after max attempts', ev4?.status === STATUS.DeadLettered,
  `status=${ev4?.status} attempts=${ev4?.attemptCount}`)

const dl = await (await get('/deadletters')).json()
const entry = dl.find(d => d.eventId === e4.eventId)
ok('dlq: listed with reason', entry !== undefined && typeof entry.reason === 'string',
  entry?.reason?.slice(0, 60))

// --- 5. replay: receiver heals, replay delivers ------------------------------
await setMode('ok')
const rp = await post(`/deadletters/${e4.eventId}/replay`)
ok('replay: accepted', rp.status === 202)
const ev4r = await waitStatus(e4.eventId, 'Delivered', 30000)
ok('replay: Delivered after heal', ev4r?.status === STATUS.Delivered,
  `status=${ev4r?.status}`)
ok('replay: body arrived', (await receiverHas(m4, 5000)) !== null)

// --- 6. metrics sanity ---------------------------------------------------------
const metrics = await (await get('/metrics')).json()
ok('metrics: retry counter moved', metrics.counters.sendsRetryable > 0,
  `sendsRetryable=${metrics.counters.sendsRetryable}`)
ok('metrics: dedup hits counted', metrics.counters.dedupHits > 0,
  `dedupHits=${metrics.counters.dedupHits}`)
// Other live dead letters may exist (stale runs) — what matters is
// OUR entry being marked replayed.
const dl2 = await (await get('/deadletters')).json()
ok('dlq: our entry marked replayed',
  dl2.find(d => d.eventId === e4.eventId)?.replayedAt != null)

// --- verdict -------------------------------------------------------------------
console.log(failures === 0
  ? `\n${tag}: ALL SCENARIOS PASSED`
  : `\n${tag}: ${failures} check(s) FAILED`)
process.exit(failures === 0 ? 0 : 1)
