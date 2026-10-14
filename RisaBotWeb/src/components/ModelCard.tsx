import { useCallback, useEffect, useRef, useState } from 'react'
import { sha256Hex } from '../api/client'
import type { ModelInfo } from '../api/types'
import { useRobot } from '../app/robot'
import { IconRotateBack, IconUpload } from './icons'
import { Badge, Btn, Card } from './ui'

function mb(bytes?: number) {
  return bytes ? `${(bytes / (1024 * 1024)).toFixed(2)} MB` : '—'
}

/**
 * A model on disk is not a model in use: dnn.load() runs once in
 * SignageDetector.__init__, so anything uploaded while the stack is up needs a
 * restart before it does anything.
 */
export function ModelCard() {
  const { client } = useRobot()
  const [info, setInfo] = useState<ModelInfo | null>(null)
  const [busy, setBusy] = useState('')
  const [progress, setProgress] = useState(0)
  const [error, setError] = useState('')
  const [note, setNote] = useState('')
  const [restart, setRestart] = useState(true)
  // Only a write tells us the file changed under a running detector; /api/model/info
  // cannot, since it reports the service's state but not when it started.
  const [needsRestart, setNeedsRestart] = useState(false)
  const fileRef = useRef<HTMLInputElement>(null)

  const refresh = useCallback(async () => {
    try {
      setInfo(await client.getModelInfo())
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [client])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const upload = async (file: File) => {
    setError('')
    setNote('')
    setProgress(0)
    setBusy('Reading file…')
    try {
      const data = await file.arrayBuffer()
      setBusy('Checksumming…')
      const sha256 = await sha256Hex(data)
      setBusy('Uploading…')
      const res = await client.uploadModel(data, {
        filename: file.name,
        sha256,
        restart,
        onProgress: setProgress,
      })
      setNeedsRestart(res.restart_required)
      setNote(
        res.restarted
          ? 'Installed and the stack restarted — the new model is loaded.'
          : 'Installed.',
      )
      if (res.warning) setError(res.warning)
      await refresh()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
    setBusy('')
    setProgress(0)
    if (fileRef.current) fileRef.current.value = ''
  }

  const rollback = async () => {
    setError('')
    setNote('')
    setBusy('Rolling back…')
    try {
      const res = await client.rollbackModel(restart)
      setNeedsRestart(res.restart_required)
      setNote(
        res.restarted
          ? 'Previous model restored and the stack restarted.'
          : 'Previous model restored.',
      )
      await refresh()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
    setBusy('')
  }


  return (
    <Card
      title="AI model"
      right={
        info ? (
          <Badge tone={info.present ? 'good' : 'warn'}>
            {info.present ? 'installed' : 'missing'}
          </Badge>
        ) : null
      }
    >
      <div className="flex flex-col gap-1.5 text-xs">
        <div className="flex justify-between gap-3">
          <span className="text-slate-500">File</span>
          <span className="truncate font-medium text-slate-200">{info?.name ?? '—'}</span>
        </div>
        <div className="flex justify-between gap-3">
          <span className="text-slate-500">Size</span>
          <span className="font-medium text-slate-200">{mb(info?.size)}</span>
        </div>
        <div className="flex justify-between gap-3">
          <span className="text-slate-500">Installed</span>
          <span className="font-medium text-slate-200">{info?.mtime_iso ?? '—'}</span>
        </div>
        <div className="flex justify-between gap-3">
          <span className="shrink-0 text-slate-500">SHA-256</span>
          <span className="truncate font-mono text-[10px] text-slate-400">
            {info?.sha256 ? `${info.sha256.slice(0, 16)}…` : '—'}
          </span>
        </div>
      </div>

      {needsRestart && (
        <p className="mt-3 rounded-lg border border-warn/30 bg-warn/10 p-2.5 text-[11px] leading-relaxed text-warn">
          This model is on disk but not in use. The detector loads the file once at startup —
          restart the stack from Controls before it takes effect.
        </p>
      )}

      <p className="mt-3 rounded-lg border border-edge bg-surface p-2.5 text-[11px] leading-relaxed text-slate-400">
        Signs are matched by class <em>index</em>, not name. A model whose classes are in a
        different order loads without complaint and acts on the wrong signs.
      </p>

      {error && (
        <p className="mt-3 rounded-lg border border-bad/40 bg-bad/10 p-2.5 text-[11px] text-bad">
          {error}
        </p>
      )}
      {note && (
        <p className="mt-3 rounded-lg border border-good/40 bg-good/10 p-2.5 text-[11px] text-good">
          {note}
        </p>
      )}

      {busy && (
        <div className="mt-3">
          <div className="flex justify-between text-[11px] text-slate-400">
            <span>{busy}</span>
            {progress > 0 && <span>{Math.round(progress * 100)}%</span>}
          </div>
          <div className="mt-1.5 h-1.5 overflow-hidden rounded-full bg-edge">
            <div
              className="h-full rounded-full bg-accent transition-all"
              style={{ width: `${Math.max(progress * 100, progress > 0 ? 2 : 0)}%` }}
            />
          </div>
        </div>
      )}

      <label className="mt-3 flex items-center gap-2 text-xs text-slate-400">
        <input
          type="checkbox"
          checked={restart}
          onChange={(e) => setRestart(e.target.checked)}
          className="h-4 w-4 accent-[#38bdf8]"
        />
        Restart the stack afterwards
      </label>

      <input
        ref={fileRef}
        type="file"
        accept=".bin"
        hidden
        onChange={(e) => {
          const f = e.target.files?.[0]
          if (f) void upload(f)
        }}
      />

      <div className="mt-3 grid grid-cols-2 gap-2">
        <Btn tone="accent" disabled={busy !== ''} onClick={() => fileRef.current?.click()}>
          <IconUpload className="text-[15px]" />
          Upload .bin
        </Btn>
        <Btn
          tone="warn"
          disabled={busy !== '' || !info?.backup.present}
          onClick={() => void rollback()}
        >
          <IconRotateBack className="text-[15px]" />
          Roll back
        </Btn>
      </div>
      {info?.backup.present && (
        <p className="mt-2 text-[11px] text-slate-500">
          Backup from {info.backup.mtime_iso} · {mb(info.backup.size)}
        </p>
      )}
    </Card>
  )
}
