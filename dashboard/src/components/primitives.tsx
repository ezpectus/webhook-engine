import { statusIndex, statusName } from '../status'

export function StatusBadge({ s }: { s: number | string }) {
  return <span className={`badge s${statusIndex(s)}`}>{statusName(s)}</span>
}

export function shortId(id: string) {
  return id.slice(0, 8)
}

export function CopyableId({ id }: { id: string }) {
  return (
    <span
      className="mono id"
      title={`${id} — click to copy`}
      onClick={e => {
        e.stopPropagation()
        navigator.clipboard?.writeText(id)
      }}
    >
      {shortId(id)}
    </span>
  )
}

export function HttpCode({ code }: { code: number | null }) {
  if (code == null) return <span className="code-none">—</span>
  const ok = code >= 200 && code < 300
  return <span className={ok ? 'code-ok' : 'code-bad'}>{code}</span>
}

export function time(iso: string) {
  return new Date(iso).toLocaleTimeString()
}
