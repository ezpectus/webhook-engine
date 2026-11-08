// Pure formatting helpers shared across components.
export const shortId = (id: string): string => id.slice(0, 8)

export const time = (iso: string): string =>
  new Date(iso).toLocaleTimeString()
