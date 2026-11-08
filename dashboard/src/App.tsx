import { useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import './App.css'
import { getStats, getMetrics, getEvents, getEvent, getDeadLetters, replay, type Stats, type Metrics, type EngineEvent, type EventDetail, type DeadLetter } from './api'

const STATUSES = ['Pending', 'InFlight', 'RetryScheduled', 'Delivered', 'DeadLettered']

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
  const timer = useRef<ReturnType<typeof setInterval>>(null)

  const refresh = async () => {
    try {
      setStats(await getStats())
      setMetrics(await getMetrics())
      setEvents(await getEvents())
      setDlq(await getDeadLetters())
    } catch {
      // API offline — покажем пустое
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
    await replay(eventId)
    refresh()
  }

  return (
    <div className="page">
      <header>
        <h1>Webhook Engine</h1>
        <span className={connected ? 'dot on' : 'dot off'} title={connected ? 'live' : 'offline'} />
      </header>

      <section className="cards">
        {STATUSES.map(s => (
          <div className="card" key={s}>
            <div className="card-num">{stats?.byStatus[s] ?? 0}</div>
            <div className="card-label">{s}</div>
          </div>
        ))}
        <div className="card alert">
          <div className="card-num">{stats?.liveDeadLetters ?? 0}</div>
          <div className="card-label">DLQ live</div>
        </div>
      </section>

      <section className="metrics">
        {metrics && (<>
          <span>dedup hits <b>{(metrics.dedup_hit_ratio * 100).toFixed(1)}%</b></span>
          <span>retry rate <b>{(metrics.delivery_retry_rate * 100).toFixed(1)}%</b></span>
          <span>rejected <b>{metrics.counters.eventsRejected}</b></span>
          <span>limiter pauses <b>{metrics.counters.limiterDelays}</b></span>
          <span>stale reclaims <b>{metrics.counters.staleReclaims}</b></span>
        </>)}
      </section>

      <div className="cols">
        <section>
          <h2>Recent events</h2>
          <table>
            <thead>
              <tr><th>Event</th><th>Status</th><th>Tries</th><th>Updated</th></tr>
            </thead>
            <tbody>
              {events.map(e => (
                <tr key={e.id} className="clickable" onClick={() => getEvent(e.id).then(setSelected).catch(() => {})}>
                  <td className="mono">{e.id.slice(0, 8)}</td>
                  <td>{e.status}</td>
                  <td>{e.attemptCount}</td>
                  <td>{new Date(e.updatedAt).toLocaleTimeString()}</td>
                </tr>
              ))}
              {events.length === 0 && <tr><td colSpan={4} className="empty">empty</td></tr>}
            </tbody>
          </table>

          {selected && (
            <section className="attempts">
              <h2>Attempts — <span className="mono">{selected.id.slice(0, 8)}</span>
                <button style={{ marginLeft: 12 }} onClick={() => setSelected(null)}>close</button>
              </h2>
              <table>
                <thead>
                  <tr><th>#</th><th>At</th><th>HTTP</th><th>ms</th><th>Error</th></tr>
                </thead>
                <tbody>
                  {selected.attempts.map(a => (
                    <tr key={a.attemptNumber}>
                      <td>{a.attemptNumber}</td>
                      <td>{new Date(a.attemptedAt).toLocaleTimeString()}</td>
                      <td>{a.responseStatusCode ?? '—'}</td>
                      <td>{a.durationMs}</td>
                      <td>{a.error ?? '—'}</td>
                    </tr>
                  ))}
                  {selected.attempts.length === 0 && <tr><td colSpan={5} className="empty">no attempts yet</td></tr>}
                </tbody>
              </table>
            </section>
          )}

          <h2 style={{ marginTop: 32 }}>Dead letters</h2>
          <table>
            <thead>
              <tr><th>Event</th><th>Reason</th><th>At</th><th></th></tr>
            </thead>
            <tbody>
              {dlq.map(d => (
                <tr key={d.eventId} className={d.replayedAt ? 'replayed' : ''}>
                  <td className="mono">{d.eventId.slice(0, 8)}</td>
                  <td>{d.reason}</td>
                  <td>{new Date(d.deadLetteredAt).toLocaleTimeString()}</td>
                  <td>
                    {d.replayedAt
                      ? 'replayed'
                      : <button onClick={() => onReplay(d.eventId)}>replay</button>}
                  </td>
                </tr>
              ))}
              {dlq.length === 0 && <tr><td colSpan={4} className="empty">empty</td></tr>}
            </tbody>
          </table>
        </section>

        <section>
          <h2>Live feed</h2>
          <ul className="feed">
            {feed.map((f, i) => (
              <li key={i}>
                <span className="mono">{f.eventId.slice(0, 8)}</span>
                {' → '}
                <b>{f.status}</b>
                <em> {f.at.toLocaleTimeString()}</em>
              </li>
            ))}
            {feed.length === 0 && <li className="empty">waiting for events…</li>}
          </ul>
        </section>
      </div>
    </div>
  )
}
