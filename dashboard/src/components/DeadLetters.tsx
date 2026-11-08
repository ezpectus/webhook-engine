import type { DeadLetter } from '../api'
import { CopyableId, time } from './primitives'

interface Props {
  dlq: DeadLetter[]
  onReplay: (eventId: string) => void
}

export function DeadLetters({ dlq, onReplay }: Props) {
  return (
    <>
      <h2 className="section-title">Dead letters</h2>
      <div className="table-wrap">
        <table>
          <thead>
            <tr><th>Event</th><th>Reason</th><th>At</th><th></th></tr>
          </thead>
          <tbody>
            {dlq.map(d => (
              <tr key={`${d.eventId}-${d.deadLetteredAt}`} className={d.replayedAt ? 'replayed' : ''}>
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
            {dlq.length === 0 && (
              <tr><td colSpan={4} className="empty">empty — nothing quarantined</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </>
  )
}
