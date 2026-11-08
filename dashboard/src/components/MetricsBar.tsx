import type { Metrics } from '../api'

export function MetricsBar({ metrics }: { metrics: Metrics | null }) {
  if (!metrics) return <section className="metrics" />
  return (
    <section className="metrics">
      <span>dedup hits <b>{(metrics.dedup_hit_ratio * 100).toFixed(1)}%</b></span>
      <span>retry rate <b>{(metrics.delivery_retry_rate * 100).toFixed(1)}%</b></span>
      <span>queue depth <b>{metrics.queue_depth}</b></span>
      <span>rejected <b>{metrics.counters.eventsRejected}</b></span>
      <span>limiter pauses <b>{metrics.counters.limiterDelays}</b></span>
      <span>stale reclaims <b>{metrics.counters.staleReclaims}</b></span>
    </section>
  )
}
