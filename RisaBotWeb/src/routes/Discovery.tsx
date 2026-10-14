import { useCallback, useEffect, useRef, useState } from 'react'
import { createClient } from '../api/demo'
import { scanForRobots } from '../api/discovery'
import type { DiscoveredRobot, PairedRobot } from '../api/types'
import { useRobot } from '../app/robot'
import { IconChevronRight, IconChip, IconRefresh } from '../components/icons'
import { Badge, Btn, Card, PageHeader } from '../components/ui'

function toPairing(host: string, r: DiscoveredRobot['info']): PairedRobot {
  return {
    host,
    serial: r.serial,
    name: r.name,
    model: r.model,
    firmware: r.firmware_version,
    mdns: r.mdns_hostname,
    pairedAt: new Date().toISOString(),
  }
}

export function Discovery() {
  const { go, pair, paired } = useRobot()
  const [found, setFound] = useState<DiscoveredRobot[]>([])
  const [scanning, setScanning] = useState(false)
  const [selected, setSelected] = useState<string | null>(null)
  const [manual, setManual] = useState('')
  const [showManual, setShowManual] = useState(false)
  const [error, setError] = useState('')
  const abortRef = useRef<AbortController | null>(null)

  const scan = useCallback(async () => {
    // A scan sweeps a whole /24; a second one must not run alongside the first.
    abortRef.current?.abort()
    const ctrl = new AbortController()
    abortRef.current = ctrl

    setScanning(true)
    setError('')
    setFound([])
    const results = await scanForRobots(
      paired,
      // A repeat serial is the same robot on a better address — scanForRobots only
      // reports one when it beats what it already had, so replace rather than skip.
      (r) =>
        setFound((prev) =>
          prev.some((p) => p.info.serial === r.info.serial)
            ? prev.map((p) => (p.info.serial === r.info.serial ? r : p))
            : [...prev, r],
        ),
      ctrl.signal,
    )
    if (ctrl.signal.aborted) return
    setFound(results)
    setSelected(results.find((r) => r.isPaired)?.host ?? results[0]?.host ?? null)
    if (results.length === 0) {
      setError('No robots answered. Connect to the robot hotspot, or enter its address.')
      setShowManual(true)
    }
    setScanning(false)
  }, [paired])

  useEffect(() => {
    void scan()
    return () => abortRef.current?.abort()
  }, [scan])

  const connect = (robot: DiscoveredRobot) => {
    pair(toPairing(robot.host, robot.info))
    go('wifi')
  }

  const connectManual = async () => {
    const host = manual.trim()
    if (!host) return
    setError('')
    try {
      // createClient, not `new RobotClient` — the scan above already honours
      // demo mode, and this path did not. Typing an address into the hosted
      // demo therefore always failed after a 5 s wait, which reads as a broken
      // app rather than as a demo declining to reach hardware it has none of.
      const info = await createClient(host).getRobotInfo(5000)
      pair(toPairing(host, info))
      go('wifi')
    } catch {
      setError(`No robot answered at ${host}:8000`)
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Robot Discovery"
        subtitle={
          scanning
            ? 'Probing the network…'
            : found.length
              ? `${found.length} robot${found.length > 1 ? 's' : ''} found${
                  paired ? ` · ${paired.name} is remembered` : ''
                }`
              : 'Nothing found yet'
        }
        right={
          scanning ? (
            <span className="flex items-center gap-1.5 rounded-full border border-accent/40 bg-accent/10 px-2.5 py-1 text-[9px] font-semibold uppercase tracking-[0.12em] text-accent">
              <span className="relative flex h-1.5 w-1.5">
                <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-current" />
                <span className="relative inline-flex h-1.5 w-1.5 rounded-full bg-current" />
              </span>
              scanning
            </span>
          ) : undefined
        }
      />

      {error && (
        <div className="rounded-xl border border-bad/40 bg-bad/10 p-3 text-xs text-bad">{error}</div>
      )}

      <div className="flex flex-col gap-2">
        {found.map((r) => (
          <button
            key={r.info.serial}
            onClick={() => setSelected(r.host)}
            onDoubleClick={() => connect(r)}
            className={`raised rounded-2xl border p-3.5 text-left transition duration-150 active:scale-[0.99] ${
              selected === r.host
                ? 'border-accent/60 bg-accent/10 shadow-[0_0_16px] shadow-accent/10'
                : 'border-edge bg-gradient-to-b from-panel-hi to-panel'
            }`}
          >
            <div className="flex items-center gap-3">
              <span
                className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border text-xl ${
                  selected === r.host
                    ? 'border-accent/40 bg-accent/15 text-accent'
                    : 'border-edge bg-surface text-slate-500'
                }`}
              >
                <IconChip />
              </span>
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <span className="truncate text-sm font-semibold">{r.info.name}</span>
                  {r.isPaired ? (
                    <Badge tone="accent">saved</Badge>
                  ) : r.isHotspot ? (
                    <Badge tone="warn">hotspot</Badge>
                  ) : (
                    <Badge tone="good">online</Badge>
                  )}
                </div>
                {/* The badge above already says hotspot vs online, which is the
                    part an operator picks a robot on. The address added nothing
                    but a thing to leak. Serial disambiguates two of the same
                    model far better than an IP does anyway. */}
                <div className="readout mt-0.5 truncate text-xs text-slate-400">
                  {r.info.model} · #{r.info.serial}
                </div>
              </div>
              <IconChevronRight
                className={`shrink-0 text-[16px] ${selected === r.host ? 'text-accent' : 'text-slate-600'}`}
              />
            </div>
          </button>
        ))}

        {scanning && found.length === 0 && (
          <div className="card flex flex-col items-center gap-3 p-8">
            <span className="relative flex h-12 w-12 items-center justify-center">
              <span className="absolute inset-0 animate-ping rounded-full border border-accent/40" />
              <span className="absolute inset-2 rounded-full border border-accent/20" />
              <IconChip className="text-[20px] text-accent" />
            </span>
            <p className="text-center text-xs text-slate-500">
              Probing the hotspot and risabot1–10.local, then sweeping this subnet…
            </p>
          </div>
        )}
      </div>

      <div className="flex gap-2">
        <Btn onClick={() => void scan()} disabled={scanning} className="flex-1">
          <IconRefresh className={`text-[15px] ${scanning ? 'animate-spin' : ''}`} />
          {scanning ? 'Scanning…' : 'Rescan'}
        </Btn>
        <Btn
          tone="accent"
          className="flex-1"
          disabled={!selected}
          onClick={() => {
            const r = found.find((f) => f.host === selected)
            if (r) connect(r)
          }}
        >
          Connect
        </Btn>
      </div>

      <button
        onClick={() => setShowManual((s) => !s)}
        className="text-xs text-slate-500 underline underline-offset-4"
      >
        {showManual ? 'Hide manual entry' : 'Enter a hostname instead'}
      </button>

      {showManual && (
        <Card title="Manual address">
          <div className="flex gap-2">
            <input
              value={manual}
              onChange={(e) => setManual(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && void connectManual()}
              placeholder="192.168.4.1 or risabot10.local"
              className="min-w-0 flex-1 rounded-xl border border-edge bg-surface px-3 py-2.5 text-sm outline-none focus:border-accent"
            />
            <Btn tone="accent" onClick={() => void connectManual()}>
              Go
            </Btn>
          </div>
        </Card>
      )}
    </div>
  )
}
