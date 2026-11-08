import type { EngineEvent } from '../api'
import { STATUS_NAMES, statusName } from '../status'
import { CopyableId, shortId, StatusBadge, time } from './primitives'

interface Props {
  events: EngineEvent[]
  filter: string
  onFilter: (status: string) => void
  onSelect: (eventId: string) => void
}

export function EventsTable({ events, filter, onFilter, onSelect }: Props) {
  const shown = filter === '' ? events : events.filter(e => statusName(e.status) === filter)

  return (
    <>
      <div className="toolbar">
        <h2 style={{ margin: 0 }}>Recent events</h2>
        <span className="spacer" style={{ flex: 1 }} />
        <select value={filter} onChange={e => onFilter(e.target.value)}>
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
              <tr key={e.id} className="clickable" onClick={() => onSelect(e.id)}>
                <td><CopyableId id={e.id} /></td>
                <td className="mono muted">{shortId(e.webhookId)}</td>
                <td><StatusBadge s={e.status} /></td>
                <td className="num">{e.attemptCount}</td>
                <td className="muted num">{time(e.updatedAt)}</td>
              </tr>
            ))}
            {shown.length === 0 && (
              <tr><td colSpan={5} className="empty">no events{filter ? ` with status ${filter}` : ''}</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </>
  )
}
