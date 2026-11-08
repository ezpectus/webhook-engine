import { useCallback, useEffect, useState } from 'react'
import { getStats, getMetrics, getEvents, getDeadLetters, type Stats, type Metrics, type EngineEvent, type DeadLetter } from '../api'

// Polls the four read endpoints in parallel on an interval.
// On failure keeps the last good snapshot — the header timestamp
// shows how stale the view is.
export function useDashboardData(intervalMs: number) {
  const [stats, setStats] = useState<Stats | null>(null)
  const [metrics, setMetrics] = useState<Metrics | null>(null)
  const [events, setEvents] = useState<EngineEvent[]>([])
  const [dlq, setDlq] = useState<DeadLetter[]>([])
  const [lastRefresh, setLastRefresh] = useState<Date | null>(null)

  const refresh = useCallback(async () => {
    try {
      const [s, m, e, d] = await Promise.all([
        getStats(), getMetrics(), getEvents(), getDeadLetters(),
      ])
      setStats(s)
      setMetrics(m)
      setEvents(e)
      setDlq(d)
      setLastRefresh(new Date())
    } catch {
      // API offline — keep whatever is already on screen
    }
  }, [])

  useEffect(() => {
    refresh()
    const t = setInterval(refresh, intervalMs)
    return () => clearInterval(t)
  }, [refresh, intervalMs])

  return { stats, metrics, events, dlq, lastRefresh, refresh }
}
