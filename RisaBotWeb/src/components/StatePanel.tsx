import type { Telemetry } from '../api/types'

/** Each tone is a text colour, a border, and the wash painted behind the panel. */
const STATE_TONE: Record<string, { text: string; ring: string; wash: string }> = {
  EMERGENCY_STOP: {
    text: 'text-bad',
    ring: 'border-bad/40',
    wash: 'from-bad/25 via-bad/5 to-transparent',
  },
  TRAFFIC_LIGHT: {
    text: 'text-warn',
    ring: 'border-warn/40',
    wash: 'from-warn/25 via-warn/5 to-transparent',
  },
  BOOM_GATE: {
    text: 'text-warn',
    ring: 'border-warn/40',
    wash: 'from-warn/25 via-warn/5 to-transparent',
  },
  OBSTRUCTION: {
    text: 'text-warn',
    ring: 'border-warn/40',
    wash: 'from-warn/25 via-warn/5 to-transparent',
  },
  LANE_RECOVERY: {
    text: 'text-warn',
    ring: 'border-warn/40',
    wash: 'from-warn/25 via-warn/5 to-transparent',
  },
  FINISHED: {
    text: 'text-good',
    ring: 'border-good/40',
    wash: 'from-good/25 via-good/5 to-transparent',
  },
  MANUAL: {
    text: 'text-slate-300',
    ring: 'border-edge',
    wash: 'from-slate-500/15 via-slate-500/5 to-transparent',
  },
}

const ACCENT = {
  text: 'text-accent',
  ring: 'border-accent/40',
  wash: 'from-accent/25 via-accent/5 to-transparent',
}

export function StatePanel({ t }: { t: Telemetry }) {
  const tone = STATE_TONE[t.state] ?? ACCENT
  const words = t.state.replace(/_/g, ' ')

  return (
    <section
      className={`raised relative overflow-hidden rounded-2xl border bg-panel p-5 ${tone.ring}`}
    >
      <div className={`absolute inset-0 bg-gradient-to-br ${tone.wash}`} />

      <div className="relative">
        <div className="flex items-center justify-between gap-3">
          <div className="flex items-center gap-1.5">
            <span className="rounded-md border border-white/10 bg-black/25 px-2 py-1 text-[10px] font-semibold uppercase tracking-[0.12em] text-slate-300">
              Lap {t.lap}
            </span>
            <span
              className={`rounded-md border border-white/10 bg-black/25 px-2 py-1 text-[10px] font-semibold uppercase tracking-[0.12em] ${
                t.auto_mode ? 'text-accent' : 'text-slate-400'
              }`}
            >
              {t.auto_mode ? 'Auto' : 'Manual'}
            </span>
          </div>
          <span className={`relative flex h-2.5 w-2.5 ${tone.text}`}>
            <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-current opacity-60" />
            <span className="relative inline-flex h-2.5 w-2.5 rounded-full bg-current" />
          </span>
        </div>

        <h2
          className={`mt-3 text-[1.75rem] font-bold leading-none tracking-tight ${tone.text}`}
        >
          {words}
        </h2>

        <div className="mt-4 flex items-center gap-4 border-t border-white/10 pt-3">
          <Metric label="in state" value={`${t.state_time}s`} />
          <span className="h-6 w-px bg-white/10" />
          <Metric label="travelled" value={`${t.state_dist} m`} />
          {t.stop_reason && (
            <>
              <span className="h-6 w-px bg-white/10" />
              <span className="min-w-0 flex-1 truncate text-xs text-slate-300">
                {t.stop_reason}
              </span>
            </>
          )}
        </div>
      </div>
    </section>
  )
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className="readout text-sm font-semibold text-slate-100">{value}</div>
      <div className="text-[10px] uppercase tracking-[0.12em] text-slate-500">{label}</div>
    </div>
  )
}

export function MetricGrid({ t }: { t: Telemetry }) {
  const cells = [
    ['Speed', `${t.speed.toFixed(2)} m/s`],
    ['Distance', `${t.distance.toFixed(1)} m`],
    ['Lane error', `${t.lane_error.toFixed(1)}°`],
    ['Pitch', `${t.imu_pitch.toFixed(1)}°`],
  ]
  return (
    <div className="grid grid-cols-2 gap-3">
      {cells.map(([label, value]) => (
        <div key={label} className="card p-4">
          <div className="text-[10px] uppercase tracking-[0.12em] text-slate-500">{label}</div>
          <div className="readout mt-1 text-xl font-semibold">{value}</div>
        </div>
      ))}
    </div>
  )
}

export function LaneErrorBar({ error }: { error: number }) {
  const clamped = Math.max(-45, Math.min(45, error))
  const pct = 50 + (clamped / 45) * 50
  return (
    <div className="card p-4">
      <div className="flex items-center justify-between">
        <h2 className="flex items-center gap-2 text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-400">
          <span className="h-3 w-0.5 rounded-full bg-accent/50" />
          Lane error
        </h2>
        <span className="readout text-sm font-semibold text-slate-100">{error.toFixed(1)}°</span>
      </div>
      <div className="relative mt-3.5 h-2 rounded-full bg-surface ring-1 ring-inset ring-edge/60">
        <div className="absolute left-1/2 h-2 w-px -translate-x-1/2 bg-edge" />
        <div
          className="absolute top-1/2 h-3.5 w-3.5 -translate-x-1/2 -translate-y-1/2 rounded-full bg-accent shadow-[0_0_10px] shadow-accent/50 transition-all duration-150"
          style={{ left: `${pct}%` }}
        />
      </div>
    </div>
  )
}

const SENSORS: [keyof Telemetry, string][] = [
  ['fused_obstacle', 'Obstacle'],
  ['boom_gate', 'Gate'],
  ['tunnel_detected', 'Tunnel'],
  ['parking_sign_detected', 'Park sign'],
]

export function SensorRow({ t }: { t: Telemetry }) {
  return (
    <div className="grid grid-cols-4 gap-2">
      {SENSORS.map(([key, label]) => {
        const on = Boolean(t[key])
        return (
          <div
            key={label}
            className={`rounded-xl border p-3 text-center ${
              on ? 'border-warn/40 bg-warn/15 text-warn' : 'border-edge bg-panel text-slate-500'
            }`}
          >
            <div className="mx-auto h-2.5 w-2.5 rounded-full bg-current" />
            <div className="mt-2 text-[11px] leading-tight">{label}</div>
          </div>
        )
      })}
    </div>
  )
}
