import { useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import { getStats, getMetrics, getEvents, getEvent, getDeadLetters, replay, type Stats, type Metrics, type EngineEvent, type EventDetail, type DeadLetter } from './api'
import './App.css'

const STATUS_NAMES = ['Pending', 'InFlight', 'RetryScheduled', 'Delivered', 'DeadLettered']

// API serializes the enum as int in /events, as a name in the SignalR feed.
const statusName = (s: number | string) =>
  typeof s === 'number' ? STATUS_NAMES[s] ?? `#${s}` : s

function StatusBadge({ s }: { s: number | string }) {
  const name = statusName(s)
  const idx = typeof s === 'number' ? s : STATUS_NAMES.indexOf(name)
  return <span className={`badge s${idx}`}>{name}</span>
}

function shortId(id: string) { return id.slice(0, 8) }

function CopyableId({ id }: { id: string }) {
  return (
    <span className="mono id" title={`${id} — click to copy`}
      onClick={e => { e.stopPropagation(); navigator.clipboard?.writeText(id) }}>
      {shortId(id)}
    </span>
  )
}

function HttpCode({ code }: { code: number | null }) {
  if (code == null) return <span className="code-none">—</span>
  const ok = code >= 200 && code < 300
  return <span className={ok ? 'code-ok' : 'code-bad'}>{code}</span>
}

function time(iso: string) { return new Date(iso).toLocaleTimeString() }

interface FeedItem {
  eventId: string
  status: string
  at: Date
}

export default function App() {
  const [stats, setStats] = useState<Stats | null>(null)
  const [metrics, setMetrics] = useState<Metrics | null>(null)
  const [events, setEvents] = useState<EngineEvent[]>([])
  const [selected, setSelected] = useState<EventDetail | null>(null)
  const [dlq, setDlq] = useState<DeadLetter[]>([])
  const [feed, setFeed] = useState<FeedItem[]>([])
  const [connected, setConnected] = useState(false)
  const [filter, setFilter] = useState('')
  const [lastRefresh, setLastRefresh] = useState<Date | null>(null)
  const timer = useRef<ReturnType<typeof setInterval>>(null)

  const refresh = async () => {
    try {
      setStats(await getStats())
      setMetrics(await getMetrics())
      setEvents(await getEvents())
      setDlq(await getDeadLetters())
      setLastRefresh(new Date())
    } catch {
      // API offline — show whatever is already on screen
    }
  }

  useEffect(() => {
    refresh()
    timer.current = setInterval(refresh, 2000)
    return () => { if (timer.current) clearInterval(timer.current) }
  }, [])

  useEffect(() => {
    const conn = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/status')
      .withAutomaticReconnect()
      .build()
    conn.on('status', (u: { eventId: string; status: string }) =>
      setFeed(f => [{ ...u, at: new Date() }, ...f].slice(0, 50)))
    conn.onreconnecting(() => setConnected(false))
    conn.onreconnected(() => setConnected(true))
    conn.start().then(() => setConnected(true)).catch(() => {})
    return () => { conn.stop() }
  }, [])

  const onReplay = async (eventId: string) => {
    const r = await replay(eventId)
    if (r.ok) refresh()
  }

  const shown = filter === '' ? events : events.filter(e => statusName(e.status) === filter)

  return (
    <div className="page">
      <header>
        <h1>Webhook Engine</h1>
        <span className="sub">delivery pipeline · refreshes every 2s{lastRefresh ? ` · updated ${lastRefresh.toLocaleTimeString()}` : ''}</span>
        <span className="spacer" />
        <span className="conn"><span className={connected ? 'dot on' : 'dot off'} />{connected ? 'live feed' : 'feed offline'}</span>
      </header>

      <section className="cards">
        {STATUS_NAMES.map((s, i) => (
          <div className={`card c${i}`} key={s}>
            <div className="card-num">{stats?.byStatus[s] ?? 0}</div>
            <div className="card-label">{s}</div>
          </div>
        ))}
        <div className="card c5">
          <div className="card-num">{stats?.liveDeadLetters ?? 0}</div>
          <div className="card-label">DLQ live</div>
        </div>
      </section>

      <section className="metrics">
        {metrics && (<>
          <span>dedup hits <b>{(metrics.dedup_hit_ratio * 100).toFixed(1)}%</b></span>
          <span>retry rate <b>{(metrics.delivery_retry_rate * 100).toFixed(1)}%</b></span>
          <span>queue depth <b>{metrics.queue_depth}</b></span>
          <span>rejected <b>{metrics.counters.eventsRejected}</b></span>
          <span>limiter pauses <b>{metrics.counters.limiterDelays}</b></span>
          <span>stale reclaims <b>{metrics.counters.staleReclaims}</b></span>
        </>)}
      </section>

      <div className="cols">
        <section>
          <div className="toolbar">
            <h2 style={{ margin: 0 }}>Recent events</h2>
            <span className="spacer" style={{ flex: 1 }} />
            <select value={filter} onChange={e => setFilter(e.target.value)}>
              <option value="">all statuses</option>
              {STATUS_NAMES.map(s => <option key={s} value={s}>{s}</option>)}
            </select>
            <span className="count">{shown.length} / {events.length}</span>
          </div>
          <div className="table-wrap">
            <table>
              <thead>
                <tr><th>Event</th><th>Webhook</th><th>Status</th><th>Tries</th><th>Updated</th></tr>
              </thead>
              <tbody>
                {shown.map(e => (
                  <tr key={e.id} className="clickable" onClick={() => getEvent(e.id).then(setSelected).catch(() => {})}>
                    <td><CopyableId id={e.id} /></td>
                    <td className="mono muted">{shortId(e.webhookId)}</td>
                    <td><StatusBadge s={e.status} /></td>
                    <td className="num">{e.attemptCount}</td>
                    <td className="muted num">{time(e.updatedAt)}</td>
                  </tr>
                ))}
                {shown.length === 0 && <tr><td colSpan={5} className="empty">no events{filter ? ` with status ${filter}` : ''}</td></tr>}
              </tbody>
            </table>
          </div>

          {selected && (
            <section className="attempts">
              <div className="panel-head">
                <h2>Attempts — <span className="mono">{shortId(selected.id)}</span></h2>
                <StatusBadge s={selected.status} />
                <button onClick={() => setSelected(null)}>close</button>
              </div>
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr><th>#</th><th>At</th><th>HTTP</th><th>ms</th><th>Error</th></tr>
                  </thead>
                  <tbody>
                    {selected.attempts.map(a => (
                      <tr key={`${a.attemptNumber}-${a.attemptedAt}`}>
                        <td className="num">{a.attemptNumber}</td>
                        <td className="muted num">{time(a.attemptedAt)}</td>
                        <td><HttpCode code={a.responseStatusCode} /></td>
                        <td className="num">{a.durationMs}</td>
                        <td className="muted">{a.error ?? '—'}</td>
                      </tr>
                    ))}
                    {selected.attempts.length === 0 && <tr><td colSpan={5} className="empty">no attempts yet</td></tr>}
                  </tbody>
                </table>
              </div>
            </section>
          )}

          <h2 className="section-title">Dead letters</h2>
          <div className="table-wrap">
            <table>
              <thead>
                <tr><th>Event</th><th>Reason</th><th>At</th><th></th></tr>
              </thead>
              <tbody>
                {dlq.map(d => (
                  <tr key={d.eventId} className={d.replayedAt ? 'replayed' : ''}>
                    <td><CopyableId id={d.eventId} /></td>
                    <td className="muted">{d.reason}</td>
                    <td className="muted num">{time(d.deadLetteredAt)}</td>
                    <td>
                      {d.replayedAt
                        ? <span className="muted">replayed</span>
                        : <button onClick={() => onReplay(d.eventId)}>replay</button>}
                    </td>
                  </tr>
                ))}
                {dlq.length === 0 && <tr><td colSpan={4} className="empty">empty — nothing quarantined</td></tr>}
              </tbody>
            </table>
          </div>
        </section>

        <section>
          <h2>Live feed</h2>
          <ul className="feed">
            {feed.map((f, i) => (
              <li key={i}>
                <CopyableId id={f.eventId} />
                <span className="arrow">→</span>
                <StatusBadge s={f.status} />
                <span className="t">{f.at.toLocaleTimeString()}</span>
              </li>
            ))}
            {feed.length === 0 && <li className="empty">waiting for events…</li>}
          </ul>
        </section>
      </div>
    </div>
  )
}
