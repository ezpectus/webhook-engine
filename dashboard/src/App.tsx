import { useState } from 'react'
import { getEvent, replay, type EventDetail } from './api'
import { useDashboardData } from './hooks/useDashboardData'
import { useStatusFeed } from './hooks/useStatusFeed'
import { Header } from './components/Header'
import { StatusCards } from './components/StatusCards'
import { MetricsBar } from './components/MetricsBar'
import { EventsTable } from './components/EventsTable'
import { AttemptsPanel } from './components/AttemptsPanel'
import { DeadLetters } from './components/DeadLetters'
import { LiveFeed } from './components/LiveFeed'
import './App.css'

export default function App() {
  const { stats, metrics, events, dlq, lastRefresh, refresh } = useDashboardData(2000)
  const { feed, connected } = useStatusFeed('/hubs/status')
  const [selected, setSelected] = useState<EventDetail | null>(null)
  const [filter, setFilter] = useState('')

  const select = (id: string) =>
    getEvent(id).then(setSelected).catch(() => {})

  const onReplay = async (eventId: string) => {
    const r = await replay(eventId)
    if (r.ok) refresh()
  }

  return (
    <div className="page">
      <Header lastRefresh={lastRefresh} connected={connected} />
      <StatusCards stats={stats} />
      <MetricsBar metrics={metrics} />
      <div className="cols">
        <section>
          <EventsTable events={events} filter={filter} onFilter={setFilter} onSelect={select} />
          {selected && <AttemptsPanel event={selected} onClose={() => setSelected(null)} />}
          <DeadLetters dlq={dlq} onReplay={onReplay} />
        </section>
        <LiveFeed feed={feed} />
      </div>
    </div>
  )
}
