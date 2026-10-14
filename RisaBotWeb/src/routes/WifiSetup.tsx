import { useCallback, useEffect, useRef, useState } from 'react'
import type { ProvisionSocket } from '../api/client'
import { PROVISION_STEPS, type WifiNetwork } from '../api/types'
import { useRobot } from '../app/robot'
import { IconRefresh, IconWifi } from '../components/icons'
import { Badge, Btn, Card, PageHeader } from '../components/ui'

type StepState = 'pending' | 'active' | 'done' | 'failed'

const STALL_MS = 60_000

function bars(signal: number) {
  return Math.max(1, Math.min(4, Math.round(signal / 25)))
}

function SignalBars({ signal }: { signal: number }) {
  const n = bars(signal)
  return (
    <span className="flex items-end gap-0.5">
      {[1, 2, 3, 4].map((i) => (
        <span
          key={i}
          className={`w-1 rounded-sm ${i <= n ? 'bg-accent' : 'bg-edge'}`}
          style={{ height: `${3 + i * 2.5}px` }}
        />
      ))}
    </span>
  )
}

export function WifiSetup() {
  const { client, go, paired, pair } = useRobot()
  const [networks, setNetworks] = useState<WifiNetwork[]>([])
  const [scanning, setScanning] = useState(false)
  const [selected, setSelected] = useState<WifiNetwork | null>(null)
  const [password, setPassword] = useState('')
  const [connecting, setConnecting] = useState(false)
  const [steps, setSteps] = useState<Record<string, StepState>>({})
  const [error, setError] = useState('')
  const [uplink, setUplink] = useState<{ ssid: string; online: boolean } | null>(null)
  const socketRef = useRef<ProvisionSocket | null>(null)

  const scan = useCallback(async () => {
    setScanning(true)
    setError('')
    try {
      const [status, res] = await Promise.all([
        client.getStatus().catch(() => null),
        client.scanWifi(),
      ])
      if (status) {
        setUplink({ ssid: status.connected_ssid, online: status.internet_online })
      }
      setNetworks([...res.networks].sort((a, b) => b.signal - a.signal))
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
    setScanning(false)
  }, [client])

  useEffect(() => {
    void scan()
    return () => socketRef.current?.close()
  }, [scan])

  // After provisioning over the hotspot the robot leaves the AP, so the pairing
  // has to follow it to the address it just got on the target network.
  const finish = useCallback(
    (ip?: string) => {
      if (paired && ip) pair({ ...paired, host: ip })
      go('dashboard')
    },
    [go, paired, pair],
  )

  const join = async () => {
    if (!selected) return
    setConnecting(true)
    setError('')
    setSteps(Object.fromEntries(PROVISION_STEPS.map(([k]) => [k, 'pending' as StepState])))

    try {
      await client.connectWifi(selected.ssid, password)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
      setConnecting(false)
      return
    }

    const socket = client.openProvisionLog()
    socketRef.current = socket

    // The robot goes quiet if provisioning wedges; without this the UI would sit
    // on "active" forever.
    let stall = setTimeout(() => {
      setError('The robot stopped reporting progress. It may still have joined.')
      setConnecting(false)
      socket.close()
    }, STALL_MS)

    socket.onFrame((frame) => {
      if (frame.type === 'pong') return
      if (frame.type === 'error') {
        setError(frame.message ?? 'Provisioning failed')
        setConnecting(false)
        socket.close()
        return
      }

      clearTimeout(stall)
      stall = setTimeout(() => {
        setError('The robot stopped reporting progress. It may still have joined.')
        setConnecting(false)
        socket.close()
      }, STALL_MS)

      if (frame.status === 'failed') {
        setError(frame.message ?? 'Could not join that network')
        setSteps((prev) => {
          const next = { ...prev }
          const at = PROVISION_STEPS.findIndex(([k]) => next[k] === 'active')
          if (at >= 0) next[PROVISION_STEPS[at][0]] = 'failed'
          return next
        })
        setConnecting(false)
        socket.close()
        return
      }

      const idx = PROVISION_STEPS.findIndex(([k]) => k === frame.status)
      if (idx < 0) return

      setSteps(() => {
        const next: Record<string, StepState> = {}
        PROVISION_STEPS.forEach(([k], i) => {
          next[k] = i < idx ? 'done' : i === idx ? 'active' : 'pending'
        })
        return next
      })

      if (frame.status === 'ready') {
        setSteps(Object.fromEntries(PROVISION_STEPS.map(([k]) => [k, 'done' as StepState])))
        clearTimeout(stall)
        socket.close()
        setConnecting(false)
        setTimeout(() => finish(frame.wlan0_ip), 500)
      }
    })

    socket.onError(() => {
      clearTimeout(stall)
      setError('Lost contact with the robot. It may have joined anyway.')
      setConnecting(false)
    })
  }

  return (
    <div className="flex flex-col gap-4">
      <PageHeader
        title="Wi-Fi Setup"
        subtitle={
          uplink?.ssid
            ? `The robot is on "${uplink.ssid}"${uplink.online ? ' with internet' : ' without internet'} — continue, or move it below.`
            : scanning
              ? 'Asking the robot what it can see…'
              : 'The robot is not on a network yet.'
        }
        right={
          <span
            className={`flex h-9 w-9 items-center justify-center rounded-xl border text-lg ${
              uplink?.online
                ? 'border-good/40 bg-good/10 text-good'
                : uplink?.ssid
                  ? 'border-warn/40 bg-warn/10 text-warn'
                  : 'border-edge bg-panel text-slate-600'
            }`}
          >
            <IconWifi />
          </span>
        }
      />

      {error && (
        <div className="rounded-xl border border-bad/40 bg-bad/10 p-3 text-xs text-bad">{error}</div>
      )}

      {connecting || Object.keys(steps).length > 0 ? (
        <Card title={`Joining ${selected?.ssid ?? ''}`}>
          <div className="flex flex-col gap-2">
            {PROVISION_STEPS.map(([key, label]) => {
              const st = steps[key] ?? 'pending'
              return (
                <div key={key} className="flex items-center gap-3">
                  <span
                    className={`h-2 w-2 rounded-full ${
                      st === 'done'
                        ? 'bg-good'
                        : st === 'active'
                          ? 'animate-pulse bg-accent'
                          : st === 'failed'
                            ? 'bg-bad'
                            : 'bg-edge'
                    }`}
                  />
                  <span
                    className={`text-xs ${st === 'pending' ? 'text-slate-600' : 'text-slate-300'}`}
                  >
                    {label}
                  </span>
                </div>
              )
            })}
          </div>
        </Card>
      ) : null}

      <div className="flex flex-col gap-2">
        {networks.map((n) => {
          const active = selected?.ssid === n.ssid
          const isCurrent = uplink?.ssid === n.ssid
          return (
            <div key={n.ssid}>
              <button
                onClick={() => {
                  setSelected(active ? null : n)
                  setPassword('')
                }}
                disabled={connecting}
                className={`raised flex w-full items-center gap-3 rounded-2xl border p-3.5 text-left transition duration-150 active:scale-[0.99] disabled:opacity-50 ${
                  active
                    ? 'border-accent/60 bg-accent/10 shadow-[0_0_16px] shadow-accent/10'
                    : 'border-edge bg-gradient-to-b from-panel-hi to-panel'
                }`}
              >
                <SignalBars signal={n.signal} />
                <span className="min-w-0 flex-1 truncate text-sm">{n.ssid}</span>
                {isCurrent && <Badge tone="good">current</Badge>}
                <span className="text-[10px] uppercase tracking-wider text-slate-500">
                  {n.security === 'Open' ? 'open' : n.security}
                </span>
              </button>

              {active && !connecting && (
                <div className="mt-2 flex gap-2 px-1">
                  {n.security !== 'Open' && (
                    <input
                      type="password"
                      value={password}
                      autoFocus
                      onChange={(e) => setPassword(e.target.value)}
                      onKeyDown={(e) => e.key === 'Enter' && void join()}
                      placeholder="Password"
                      className="min-w-0 flex-1 rounded-xl border border-edge bg-surface px-3 py-2.5 text-sm outline-none focus:border-accent"
                    />
                  )}
                  <Btn tone="good" onClick={() => void join()} className={n.security === 'Open' ? 'flex-1' : ''}>
                    Join
                  </Btn>
                </div>
              )}
            </div>
          )
        })}
      </div>

      <div className="flex gap-2">
        <Btn onClick={() => void scan()} disabled={scanning || connecting} className="flex-1">
          <IconRefresh className={`text-[15px] ${scanning ? 'animate-spin' : ''}`} />
          {scanning ? 'Scanning…' : 'Rescan'}
        </Btn>
        <Btn tone="accent" className="flex-1" disabled={connecting} onClick={() => go('dashboard')}>
          {uplink?.ssid ? 'Continue' : 'Skip'}
        </Btn>
      </div>
    </div>
  )
}
