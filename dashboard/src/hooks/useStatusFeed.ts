import { useEffect, useState } from 'react'
import * as signalR from '@microsoft/signalr'

export interface FeedItem {
  eventId: string
  status: string
  at: Date
}

const FEED_LIMIT = 50

// SignalR subscription to the status hub: newest-first feed,
// capped at FEED_LIMIT items. connected mirrors the real
// connection state including automatic reconnects.
export function useStatusFeed(url: string) {
  const [feed, setFeed] = useState<FeedItem[]>([])
  const [connected, setConnected] = useState(false)

  useEffect(() => {
    const conn = new signalR.HubConnectionBuilder()
      .withUrl(url)
      .withAutomaticReconnect()
      .build()

    conn.on('status', (u: { eventId: string; status: string }) =>
      setFeed(f => [{ ...u, at: new Date() }, ...f].slice(0, FEED_LIMIT)))
    conn.onreconnecting(() => setConnected(false))
    conn.onreconnected(() => setConnected(true))
    conn.onclose(() => setConnected(false))
    conn.start().then(() => setConnected(true)).catch(() => {})

    return () => { void conn.stop() }
  }, [url])

  return { feed, connected }
}
