import { useCallback, useEffect, useState } from 'react'
import type { LoggerSession } from '../api/types'
import { useRobot } from '../app/robot'
import { Badge, Btn, Card, PageHeader, Stat } from '../components/ui'

function mmss(sec: number) {
  const s = Math.max(0, Math.floor(sec))
  return `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`
}

export function DataLogger() {
  const { client, telemetry, online } = useRobot()
  const [sessions, setSessions] = useState<LoggerSession[]>([])
  const [label, setLabel] = useState('run')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const status = telemetry?.logger_status
  const recording = Boolean(status?.is_logging)

  const refresh = useCallback(async () => {
    try {
      const res = await client.listLoggerSessions()
      setSessions(res.sessions ?? [])
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [client])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const toggle = async () => {
    setBusy(true)
    setError('')
    try {
      if (recording) {
        await client.stopLogger()
        await refresh()
      } else {
        await client.startLogger(label.trim() || 'run')
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
    setBusy(false)
  }

  return (
    <div className="flex flex-col gap-3">
      <PageHeader title="Data Logger" subtitle="Records telemetry to CSV on the robot." />

      {error && (
        <div className="rounded-xl border border-bad/40 bg-bad/10 p-3 text-xs text-bad">{error}</div>
      )}

      <Card
        title="Session"
        right={
          <Badge tone={!online ? 'default' : recording ? 'good' : 'accent'}>
            <span className={`h-1.5 w-1.5 rounded-full bg-current ${recording ? 'animate-pulse' : ''}`} />
            {!online ? 'offline' : recording ? 'logging' : 'idle'}
          </Badge>
        }
      >
        {recording && status ? (
          <div className="grid grid-cols-3 gap-2">
            <Stat label="Duration" value={mmss(status.duration_sec)} />
            <Stat label="Samples" value={status.sample_count} />
            <Stat label="Name" value={<span className="truncate text-xs">{status.session_name}</span>} />
          </div>
        ) : (
          <input
            value={label}
            onChange={(e) => setLabel(e.target.value)}
            placeholder="Session label"
            className="w-full rounded-xl border border-edge bg-surface px-3 py-2.5 text-sm outline-none focus:border-accent"
          />
        )}

        <Btn
          tone={recording ? 'bad' : 'good'}
          className="mt-3 w-full"
          disabled={busy || !online}
          onClick={() => void toggle()}
        >
          {busy ? 'Working…' : recording ? 'Stop recording' : 'Start recording'}
        </Btn>
      </Card>

      <Card
        title="Archive"
        right={
          <button onClick={() => void refresh()} className="text-[11px] text-accent underline underline-offset-4">
            refresh
          </button>
        }
      >
        {sessions.length === 0 ? (
          <p className="py-4 text-center text-xs text-slate-500">No recorded sessions.</p>
        ) : (
          <div className="flex flex-col gap-2">
            {sessions.map((s) => (
              <div
                key={s.name}
                className="flex items-center gap-3 rounded-xl border border-edge bg-surface p-3"
              >
                <div className="min-w-0 flex-1">
                  <div className="truncate text-xs text-slate-200">{s.name}</div>
                  <div className="mt-0.5 text-[10px] text-slate-500">
                    {s.time} · {mmss(s.duration)} · {s.samples} rows
                  </div>
                </div>
                <a
                  href={client.loggerDownloadUrl(s.name)}
                  download
                  className="shrink-0 rounded-lg border border-accent/40 bg-accent/15 px-3 py-1.5 text-[11px] font-medium text-accent"
                >
                  CSV
                </a>
              </div>
            ))}
          </div>
        )}
      </Card>
    </div>
  )
}
