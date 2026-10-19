import { useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import { getStats, getDeadLetters, replay, type Stats, type DeadLetter } from './api'

const STATUSES = ['Pending', 'InFlight', 'RetryScheduled', 'Delivered', 'DeadLettered']

interface FeedItem {
  eventId: string
  status: string
  at: Date
}

export default function App() {
  const [stats, setStats] = useState<Stats | null>(null)
  const [dlq, setDlq] = useState<DeadLetter[]>([])
  const [feed, setFeed] = useState<FeedItem[]>([])
  const [connected, setConnected] = useState(false)
  const timer = useRef<ReturnType<typeof setInterval>>(null)

  const refresh = async () => {
    try {
      setStats(await getStats())
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

      <div className="cols">
        <section>
          <h2>Dead letters</h2>
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
