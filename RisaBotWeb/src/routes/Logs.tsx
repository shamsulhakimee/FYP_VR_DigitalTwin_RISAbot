import { useCallback, useEffect, useState } from 'react'
import type { LogsResponse } from '../api/types'
import { useRobot } from '../app/robot'
import { IconRefresh } from '../components/icons'
import { Badge, Btn, PageHeader } from '../components/ui'

export function Logs() {
  const { client } = useRobot()
  const [data, setData] = useState<LogsResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [errorsOnly, setErrorsOnly] = useState(false)
  const [error, setError] = useState('')

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      setData(await client.getLogs())
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
    setLoading(false)
  }, [client])

  useEffect(() => {
    void load()
  }, [load])

  const errorSet = new Set(data?.errors ?? [])
  const lines = (data?.logs ?? []).filter((l) => !errorsOnly || errorSet.has(l))

  return (
    <div className="flex flex-col gap-3">
      <PageHeader
        title="Logs"
        subtitle={data?.status_summary ?? 'journalctl output from the launch unit.'}
        right={
          data ? (
            <Badge tone={data.has_error ? 'bad' : 'good'}>
              {data.has_error ? `${data.error_count} errors` : 'clean'}
            </Badge>
          ) : undefined
        }
      />

      <div className="flex gap-2">
        <Btn className="flex-1" onClick={() => void load()} disabled={loading}>
          <IconRefresh className={`text-[15px] ${loading ? 'animate-spin' : ''}`} />
          {loading ? 'Loading…' : 'Refresh'}
        </Btn>
        <Btn
          className="flex-1"
          tone={errorsOnly ? 'bad' : 'default'}
          onClick={() => setErrorsOnly((v) => !v)}
        >
          Errors only
        </Btn>
      </div>

      {error && (
        <div className="rounded-xl border border-bad/40 bg-bad/10 p-3 text-xs text-bad">{error}</div>
      )}

      <div className="raised overflow-hidden rounded-2xl border border-edge bg-panel">
        {lines.length === 0 ? (
          <p className="p-6 text-center text-xs text-slate-500">
            {loading ? 'Fetching…' : errorsOnly ? 'No errors.' : 'No log lines.'}
          </p>
        ) : (
          <div className="max-h-[65vh] overflow-auto">
            {lines.map((line, i) => (
              <div
                key={i}
                className={`border-b border-edge/50 px-3 py-2 font-mono text-[11px] leading-relaxed break-words last:border-0 ${
                  errorSet.has(line) ? 'bg-bad/5 text-bad' : 'text-slate-400'
                }`}
              >
                {line}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
