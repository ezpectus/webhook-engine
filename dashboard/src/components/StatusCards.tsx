import type { Stats } from '../api'
import { STATUS_NAMES } from '../status'

export function StatusCards({ stats }: { stats: Stats | null }) {
  return (
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
  )
}
