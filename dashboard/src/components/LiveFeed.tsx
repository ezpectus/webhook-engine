import type { FeedItem } from '../hooks/useStatusFeed'
import { CopyableId, StatusBadge } from './primitives'

export function LiveFeed({ feed }: { feed: FeedItem[] }) {
  return (
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
  )
}
