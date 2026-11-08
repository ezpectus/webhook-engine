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
    eventsAccepted: number
    eventsRejected: number
    dedupHits: number
    sendsOk: number
    sendsRetryable: number
    sendsNonRetryable: number
    limiterDelays: number
    staleReclaims: number
  }
}

export interface EngineEvent {
  id: string
  webhookId: string
  status: number // EventStatus enum serialized as int by the API
  attemptCount: number
  nextAttemptAt: string | null
  updatedAt: string
}

export const getStats = (): Promise<Stats> =>
  fetch('/stats').then(r => r.json())

export interface EventDetail extends EngineEvent {
  deliveredAt: string | null
  createdAt: string
  attempts: {
    attemptNumber: number
    attemptedAt: string
    responseStatusCode: number | null
    error: string | null
    succeeded: boolean
    durationMs: number
  }[]
}

export const getEvents = (): Promise<EngineEvent[]> =>
  fetch('/events?take=30').then(r => r.json())

export const getEvent = (id: string): Promise<EventDetail> =>
  fetch(`/events/${id}`).then(r => r.json())

export const getMetrics = (): Promise<Metrics> =>
  fetch('/metrics').then(r => r.json())

export const getDeadLetters = (): Promise<DeadLetter[]> =>
  fetch('/deadletters').then(r => r.json())

export const replay = (eventId: string): Promise<Response> =>
  fetch(`/deadletters/${eventId}/replay`, { method: 'POST' })
