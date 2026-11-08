import { useEffect, useState } from 'react'
import { getWebhooks, registerWebhook, sendEvent, type Webhook } from '../api'

// Receiver chaos modes from tools/MockReceiver — each gets its own
// webhook so a visitor can drive the whole pipeline by clicking:
// ok delivers, 500/429/slow retry and dead-letter, flap alternates.
const MODES = ['ok', '500', '429', 'slow', 'flap', 'drop'] as const

interface Props {
  onChanged: () => void
}

export function Playground({ onChanged }: Props) {
  const [webhooks, setWebhooks] = useState<Webhook[]>([])
  const [base, setBase] = useState('http://localhost:5005/hook')
  const [mode, setMode] = useState<string>('ok')
  const [busy, setBusy] = useState(false)

  const load = () => getWebhooks().then(setWebhooks).catch(() => {})
  useEffect(() => { load() }, [])

  const hookFor = (m: string) =>
    webhooks.find(w => w.url === `${base}${m === 'ok' ? '' : `?mode=${m}`}`)

  const ensure = async () => {
    setBusy(true)
    try {
      for (const m of MODES) {
        if (!hookFor(m)) {
          await registerWebhook(
            `${base}${m === 'ok' ? '' : `?mode=${m}`}`, 50)
        }
      }
      await load()
      onChanged()
    } finally { setBusy(false) }
  }

  const fire = async (n: number) => {
    const wh = hookFor(mode)
    if (!wh) return
    setBusy(true)
    try {
      for (let i = 0; i < n; i++) {
        await sendEvent(wh.id, JSON.stringify({
          demo: mode, n: `${Date.now()}-${i}`,
        }))
      }
      onChanged()
    } finally { setBusy(false) }
  }

  const ready = MODES.every(m => hookFor(m))

  return (
    <section className="playground">
      <div className="toolbar">
        <h2 style={{ margin: 0 }}>Playground</h2>
        <span className="sub">— fire real events, watch the pipeline</span>
        <span className="spacer" style={{ flex: 1 }} />
        {!ready && (
          <button onClick={ensure} disabled={busy}>
            register demo endpoints
          </button>
        )}
      </div>
      <div className="pg-row">
        <label className="muted">receiver</label>
        <input
          className="pg-base mono"
          value={base}
          onChange={e => setBase(e.target.value.replace(/\/$/, ''))}
          spellCheck={false}
        />
      </div>
      <div className="pg-row">
        <label className="muted">mode</label>
        {MODES.map(m => (
          <button
            key={m}
            className={`pg-mode${mode === m ? ' sel' : ''}`}
            onClick={() => setMode(m)}
            disabled={!hookFor(m)}
            title={hookFor(m) ? '' : 'register demo endpoints first'}
          >
            {m}
          </button>
        ))}
        <span className="spacer" style={{ flex: 1 }} />
        <button onClick={() => fire(1)} disabled={busy || !hookFor(mode)}>
          send 1
        </button>
        <button onClick={() => fire(10)} disabled={busy || !hookFor(mode)}>
          send 10
        </button>
      </div>
    </section>
  )
}
