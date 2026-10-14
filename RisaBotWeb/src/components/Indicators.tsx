const LAMPS = ['red', 'yellow', 'green'] as const

const LAMP_ON: Record<(typeof LAMPS)[number], string> = {
  red: 'bg-bad shadow-[0_0_12px_2px] shadow-bad/50',
  yellow: 'bg-warn shadow-[0_0_12px_2px] shadow-warn/50',
  green: 'bg-good shadow-[0_0_12px_2px] shadow-good/50',
}

export function TrafficLight({ lamp }: { lamp: string }) {
  const active = lamp?.toLowerCase()
  return (
    <div className="flex items-center gap-3">
      <div className="flex gap-2 rounded-lg border border-edge bg-surface px-2.5 py-2">
        {LAMPS.map((l) => (
          <span
            key={l}
            className={`h-4 w-4 rounded-full transition-all duration-200 ${
              active === l ? LAMP_ON[l] : 'bg-edge'
            }`}
          />
        ))}
      </div>
      <span className="text-xs uppercase tracking-wider text-slate-400">
        {active && active !== 'unknown' ? active : 'no signal'}
      </span>
    </div>
  )
}

/**
 * auto_driver picks exactly one behaviour per tick in this priority order, so
 * the chain shows which one currently owns /cmd_vel.
 */
const CHAIN = ['LANE_FOLLOW', 'TUNNEL', 'OBSTRUCTION', 'BOOM_GATE', 'TRAFFIC_LIGHT', 'MANUAL']

const SHORT: Record<string, string> = {
  LANE_FOLLOW: 'LANE',
  TUNNEL: 'TUNNEL',
  OBSTRUCTION: 'OBSTR',
  BOOM_GATE: 'GATE',
  TRAFFIC_LIGHT: 'LIGHT',
  MANUAL: 'MANUAL',
}

export function ActionPriorityBar({ state }: { state: string }) {
  const active = CHAIN.indexOf(state)
  return (
    <div className="no-scrollbar -mx-1 flex items-center gap-1 overflow-x-auto px-1">
      {CHAIN.map((node, i) => (
        <div key={node} className="flex shrink-0 items-center gap-1">
          <div
            className={`shrink-0 rounded-md border px-2 py-1.5 text-center text-[9px] font-semibold tracking-wider transition ${
              i === active
                ? 'border-accent bg-accent/20 text-accent'
                : active >= 0 && i < active
                  ? 'border-edge bg-surface text-slate-500'
                  : 'border-edge/50 bg-surface/40 text-slate-700'
            }`}
          >
            {SHORT[node]}
          </div>
          {i < CHAIN.length - 1 && (
            <span
              className={`text-[9px] ${active >= 0 && i < active ? 'text-slate-500' : 'text-slate-700'}`}
            >
              ›
            </span>
          )}
        </div>
      ))}
    </div>
  )
}

/**
 * Button and axis indices are fixed by the competition controller, matching the
 * mapping hard-coded in GamepadView.cs.
 */
const BUTTONS: [number, string][] = [
  [0, 'A'],
  [1, 'B'],
  [3, 'X'],
  [4, 'Y'],
  [6, 'LB'],
  [7, 'RB'],
  [8, 'LT'],
  [9, 'RT'],
  [11, 'ST'],
]

const DEADZONE = 0.05

function Stick({ x, y, label }: { x: number; y: number; label: string }) {
  const live = Math.hypot(x, y) > DEADZONE
  return (
    <div className="relative h-14 w-14 rounded-full border border-edge bg-surface">
      <div
        className={`absolute h-3 w-3 rounded-full transition-all duration-150 ${
          live ? 'bg-accent' : 'bg-slate-600'
        }`}
        style={{
          left: `calc(${50 + x * 34}% - 6px)`,
          top: `calc(${50 + y * 34}% - 6px)`,
        }}
      />
      <span className="absolute -bottom-4 left-0 w-full text-center text-[9px] text-slate-600">
        {label}
      </span>
    </div>
  )
}

export function Gamepad({ buttons, axes }: { buttons: number[]; axes: number[] }) {
  const ax = (i: number) => axes?.[i] ?? 0
  return (
    <div className="flex items-center gap-4">
      <Stick x={ax(0)} y={ax(1)} label="L" />
      <Stick x={ax(2)} y={ax(3)} label="R" />
      <div className="grid flex-1 grid-cols-5 gap-1">
        {BUTTONS.map(([i, label]) => (
          <span
            key={label}
            className={`rounded border py-1 text-center text-[9px] font-semibold transition ${
              buttons?.[i]
                ? 'border-accent/50 bg-accent/20 text-accent'
                : 'border-edge bg-surface text-slate-600'
            }`}
          >
            {label}
          </span>
        ))}
      </div>
    </div>
  )
}
