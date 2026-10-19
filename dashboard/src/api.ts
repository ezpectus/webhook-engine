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

export const getStats = (): Promise<Stats> =>
  fetch('/stats').then(r => r.json())

export const getDeadLetters = (): Promise<DeadLetter[]> =>
  fetch('/deadletters').then(r => r.json())

export const replay = (eventId: string): Promise<Response> =>
  fetch(`/deadletters/${eventId}/replay`, { method: 'POST' })
