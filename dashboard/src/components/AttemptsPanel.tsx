import type { EventDetail } from '../api'
import { HttpCode, StatusBadge } from './primitives'
import { shortId, time } from '../format'

interface Props {
  event: EventDetail
  onClose: () => void
}

export function AttemptsPanel({ event, onClose }: Props) {
  return (
    <section className="attempts">
      <div className="panel-head">
        <h2>Attempts — <span className="mono">{shortId(event.id)}</span></h2>
        <StatusBadge s={event.status} />
        <button onClick={onClose}>close</button>
      </div>
      <div className="table-wrap">
        <table>
          <thead>
            <tr><th>#</th><th>At</th><th>HTTP</th><th>ms</th><th>Error</th></tr>
          </thead>
          <tbody>
            {event.attempts.map(a => (
              <tr key={`${a.attemptNumber}-${a.attemptedAt}`}>
                <td className="num">{a.attemptNumber}</td>
                <td className="muted num">{time(a.attemptedAt)}</td>
                <td><HttpCode code={a.responseStatusCode} /></td>
                <td className="num">{a.durationMs}</td>
                <td className="muted">{a.error ?? '—'}</td>
              </tr>
            ))}
            {event.attempts.length === 0 && (
              <tr><td colSpan={5} className="empty">no attempts yet</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </section>
  )
}
