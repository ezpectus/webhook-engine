// Shared status vocabulary — API serializes the enum as int in REST
// payloads and as a name in the SignalR feed.
export const STATUS_NAMES = ['Pending', 'InFlight', 'RetryScheduled', 'Delivered', 'DeadLettered']

export const statusName = (s: number | string): string =>
  typeof s === 'number' ? STATUS_NAMES[s] ?? `#${s}` : s

export const statusIndex = (s: number | string): number =>
  typeof s === 'number' ? s : STATUS_NAMES.indexOf(statusName(s))
