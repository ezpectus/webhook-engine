// Live smoke test — zero dependencies, Node 18+ fetch.
// Verifies the whole API surface against a running stack:
//   docker compose up -d postgres redis
//   dotnet run --project src/WebhookEngine.Api
//
//   node tests/smoke.mjs
//   API=http://host:port DASH=http://host:port node tests/smoke.mjs
//
// RECEIVER = the webhook URL the *API process* must dial. Locally it is
// the host-published port; inside docker compose the API resolves the
// mockreceiver service by name — pass RECEIVER=http://mockreceiver:5005.
//
// Exit 0 = every check passed. Each check prints ok/FAIL.

const API = process.env.API ?? 'http://localhost:5269'
const DASH = process.env.DASH // optional — skipped when unset/unreachable
const RECEIVER = process.env.RECEIVER ?? 'http://localhost:5005'
const tag = `smoke-${Date.now()}`

let failures = 0
const ok = (name, cond, extra = '') => {
  console.log(`${cond ? ' ok ' : 'FAIL'}  ${name}${extra ? `  (${extra})` : ''}`)
  if (!cond) failures++
}

const get = async (path) => (await fetch(`${API}${path}`))
const post = async (path, body) =>
  fetch(`${API}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })

// --- health ---------------------------------------------------------------
const health = await (await get('/health')).json()
ok('health: postgres up', health.postgres === true, `status=${health.status}`)

// --- webhook registration --------------------------------------------------
ok('register: javascript: rejected', (await post('/webhooks', {
  url: 'javascript:alert(1)', rateLimitPerSecond: 5 })).status === 400)
ok('register: rate<=0 rejected', (await post('/webhooks', {
  url: `${API}/hook`, rateLimitPerSecond: 0 })).status === 400)

const wh = await (await post('/webhooks', {
  url: `${RECEIVER}/hook`, rateLimitPerSecond: 50 })).json()
ok('register: returns webhookId', typeof wh.webhookId === 'string')

const list = await (await get('/webhooks')).json()
ok('list: webhook visible', list.some(w => w.id === wh.webhookId))

// --- ingest ----------------------------------------------------------------
ok('ingest: non-JSON payload rejected', (await post('/events', {
  webhookId: wh.webhookId, payload: 'not json' })).status === 400)
ok('ingest: empty payload rejected', (await post('/events', {
  webhookId: wh.webhookId, payload: '' })).status === 400)
ok('ingest: unknown webhook rejected', (await post('/events', {
  webhookId: '00000000-0000-0000-0000-000000000000', payload: '{}' })).status === 400)

const payload = JSON.stringify({ smoke: tag })
const e1 = await (await post('/events', { webhookId: wh.webhookId, payload })).json()
ok('ingest: accepted with eventId', typeof e1.eventId === 'string' && e1.status === 'accepted')

const e2 = await (await post('/events', { webhookId: wh.webhookId, payload })).json()
ok('dedup: same payload -> same eventId', e2.eventId === e1.eventId && e2.status === 'duplicate',
  e2.eventStatus ? `via PG (${e2.eventStatus})` : 'via Redis')

// --- event detail ----------------------------------------------------------
await new Promise(r => setTimeout(r, 1500))
const ev = await (await get(`/events/${e1.eventId}`)).json()
ok('detail: has attempts array', Array.isArray(ev.attempts))
ok('detail: chronological order', ev.attempts.every((a, i) =>
  i === 0 || a.attemptedAt >= ev.attempts[i - 1].attemptedAt))
ok('detail: 404 on random id', (await get(
  '/events/00000000-0000-0000-0000-000000000000')).status === 404)

// --- queries / aggregates ---------------------------------------------------
ok('events: array returned', Array.isArray(await (await get('/events?take=5')).json()))
const stats = await (await get('/stats')).json()
ok('stats: byStatus map', typeof stats.byStatus === 'object')
const metrics = await (await get('/metrics')).json()
ok('metrics: ratios + counters',
  typeof metrics.dedup_hit_ratio === 'number' &&
  typeof metrics.counters.eventsAccepted === 'number')

// --- dlq / replay -----------------------------------------------------------
ok('deadletters: array returned', Array.isArray(await (await get('/deadletters')).json()))
ok('replay: 404 on random id', (await fetch(
  `${API}/deadletters/00000000-0000-0000-0000-000000000000/replay`,
  { method: 'POST' })).status === 404)

// --- signalr negotiate -------------------------------------------------------
const neg = await fetch(`${API}/hubs/status/negotiate?negotiateVersion=1`, { method: 'POST' })
ok('signalr: negotiate answers', neg.status === 200)

// --- dashboard (optional) -----------------------------------------------------
if (DASH) {
  try {
    const dash = await fetch(DASH, { signal: AbortSignal.timeout(3000) })
    ok('dashboard: serves html', dash.status === 200 &&
      (await dash.text()).includes('script'))
  } catch { ok('dashboard: reachable', false, 'unreachable') }
}

console.log(failures === 0
  ? `\n${tag}: all checks passed`
  : `\n${tag}: ${failures} check(s) FAILED`)
process.exit(failures === 0 ? 0 : 1)
