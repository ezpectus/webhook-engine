export interface Stats {
  byStatus: Record<string, number>
  liveDeadLetters: number
}

export interface DeadLetter {
  eventId: string
  reason: string
  deadLetteredAt: string
  replayedAt: string | null
}

export interface Metrics {
  dedup_hit_ratio: number
  delivery_retry_rate: number
  queue_depth: number
  dlq_live: number
  counters: {
    EventsAccepted: number
    EventsRejected: number
    DedupHits: number
    SendsOk: number
    SendsRetryable: number
    SendsNonRetryable: number
    LimiterDelays: number
    StaleReclaims: number
  }
}

export const getStats = (): Promise<Stats> =>
  fetch('/stats').then(r => r.json())

export const getMetrics = (): Promise<Metrics> =>
  fetch('/metrics').then(r => r.json())

export const getDeadLetters = (): Promise<DeadLetter[]> =>
  fetch('/deadletters').then(r => r.json())

export const replay = (eventId: string): Promise<Response> =>
  fetch(`/deadletters/${eventId}/replay`, { method: 'POST' })
