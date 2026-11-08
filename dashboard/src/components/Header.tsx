interface Props {
  lastRefresh: Date | null
  connected: boolean
}

export function Header({ lastRefresh, connected }: Props) {
  return (
    <header>
      <h1>Webhook Engine</h1>
      <span className="sub">
        delivery pipeline · refreshes every 2s
        {lastRefresh ? ` · updated ${lastRefresh.toLocaleTimeString()}` : ''}
      </span>
      <span className="spacer" />
      <span className="conn">
        <span className={connected ? 'dot on' : 'dot off'} />
        {connected ? 'live feed' : 'feed offline'}
      </span>
    </header>
  )
}
