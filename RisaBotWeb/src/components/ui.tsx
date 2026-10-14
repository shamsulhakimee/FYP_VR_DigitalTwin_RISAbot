import type { ReactNode } from 'react'

export function PageHeader({
  title,
  subtitle,
  right,
}: {
  title: string
  subtitle?: ReactNode
  right?: ReactNode
}) {
  return (
    <header className="flex items-start justify-between gap-3">
      <div className="min-w-0">
        <h1 className="flex items-center gap-2.5 text-xl font-semibold tracking-tight">
          <span className="h-5 w-1 rounded-full bg-accent shadow-[0_0_10px] shadow-accent/50" />
          {title}
        </h1>
        {subtitle && <p className="mt-1.5 pl-3.5 text-xs text-slate-400">{subtitle}</p>}
      </div>
      {right && <div className="shrink-0 pt-1">{right}</div>}
    </header>
  )
}

export function Card({
  title,
  right,
  children,
  className = '',
}: {
  title?: string
  right?: ReactNode
  children: ReactNode
  className?: string
}) {
  return (
    <section className={`card p-4 ${className}`}>
      {(title || right) && (
        <div className="mb-3 flex items-center justify-between gap-3">
          {title && (
            <h2 className="flex min-w-0 items-center gap-2 text-[11px] font-semibold uppercase tracking-[0.14em] text-slate-400">
              <span className="h-3 w-0.5 shrink-0 rounded-full bg-accent/50" />
              <span className="truncate">{title}</span>
            </h2>
          )}
          {right && <div className="ml-auto shrink-0">{right}</div>}
        </div>
      )}
      {children}
    </section>
  )
}

export function Btn({
  children,
  onClick,
  disabled,
  tone = 'default',
  className = '',
}: {
  children: ReactNode
  onClick?: () => void
  disabled?: boolean
  tone?: 'default' | 'accent' | 'good' | 'warn' | 'bad'
  className?: string
}) {
  const tones = {
    default: 'border-edge bg-gradient-to-b from-panel-hi to-panel text-slate-200',
    accent: 'border-accent/40 bg-accent/15 text-accent shadow-accent/10',
    good: 'border-good/40 bg-good/15 text-good shadow-good/10',
    warn: 'border-warn/40 bg-warn/15 text-warn shadow-warn/10',
    bad: 'border-bad/40 bg-bad/15 text-bad shadow-bad/10',
  }
  return (
    <button
      onClick={onClick}
      disabled={disabled}
      className={`raised flex items-center justify-center gap-2 rounded-xl border px-4 py-3 text-sm font-semibold transition duration-150 active:scale-[0.97] active:brightness-110 disabled:opacity-35 disabled:active:scale-100 ${tones[tone]} ${className}`}
    >
      {children}
    </button>
  )
}

export function Badge({
  children,
  tone = 'default',
}: {
  children: ReactNode
  tone?: 'default' | 'accent' | 'good' | 'warn' | 'bad'
}) {
  const tones = {
    default: 'border-edge bg-surface text-slate-400',
    accent: 'border-accent/40 bg-accent/15 text-accent',
    good: 'border-good/40 bg-good/15 text-good',
    warn: 'border-warn/40 bg-warn/15 text-warn',
    bad: 'border-bad/40 bg-bad/15 text-bad',
  }
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[11px] font-medium uppercase tracking-wider ${tones[tone]}`}
    >
      {children}
    </span>
  )
}

export function Stat({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="rounded-lg border border-edge/60 bg-surface/60 px-2.5 py-2">
      <div className="text-[10px] uppercase tracking-[0.12em] text-slate-500">{label}</div>
      <div className="readout mt-1 truncate text-base font-semibold text-slate-100">{value}</div>
    </div>
  )
}

/**
 * Signed bars grow from the centre so the sign is readable at a glance; unsigned
 * ones fill from the left. Mirrors MeterBar.cs, transition included.
 */
export function MeterBar({
  value,
  max,
  signed = false,
  tone = 'accent',
}: {
  value: number
  max: number
  signed?: boolean
  tone?: 'accent' | 'good' | 'warn' | 'bad'
}) {
  const clamped = Math.max(signed ? -max : 0, Math.min(max, value))
  const frac = max === 0 ? 0 : clamped / max
  const fill = {
    accent: 'bg-accent shadow-[0_0_8px] shadow-accent/40',
    good: 'bg-good shadow-[0_0_8px] shadow-good/40',
    warn: 'bg-warn shadow-[0_0_8px] shadow-warn/40',
    bad: 'bg-bad shadow-[0_0_8px] shadow-bad/40',
  }[tone]

  return (
    <div className="relative h-1.5 rounded-full bg-surface ring-1 ring-inset ring-edge/60">
      {signed && <div className="absolute left-1/2 h-full w-px bg-edge" />}
      <div
        className={`absolute h-full rounded-full transition-[left,width] duration-300 ease-out ${fill}`}
        style={
          signed
            ? {
                left: `${50 + Math.min(frac, 0) * 50}%`,
                width: `${Math.abs(frac) * 50}%`,
              }
            : { left: 0, width: `${frac * 100}%` }
        }
      />
    </div>
  )
}

export function MeterRow({
  label,
  value,
  unit = '',
  max,
  signed = false,
  tone = 'accent',
  digits = 2,
}: {
  label: string
  value: number
  unit?: string
  max: number
  signed?: boolean
  tone?: 'accent' | 'good' | 'warn' | 'bad'
  digits?: number
}) {
  return (
    <div className="mb-3 last:mb-0">
      <div className="mb-1.5 flex items-baseline justify-between text-xs">
        <span className="text-slate-400">{label}</span>
        <span className="readout font-semibold text-slate-100">
          {value.toFixed(digits)}
          <span className="text-slate-500">{unit}</span>
        </span>
      </div>
      <MeterBar value={value} max={max} signed={signed} tone={tone} />
    </div>
  )
}

/**
 * `trueIsGood` decides the colour, not the value: an open boom gate is green
 * while a detected obstacle is red, and both are `true`.
 */
export function SensorDot({
  label,
  status,
  trueIsGood = false,
  trueText = 'YES',
  falseText = 'NO',
}: {
  label: string
  status: boolean | null | undefined
  trueIsGood?: boolean
  trueText?: string
  falseText?: string
}) {
  const unknown = status === null || status === undefined
  const good = unknown ? false : status === trueIsGood
  const colour = unknown ? 'text-slate-600' : good ? 'text-good' : 'text-bad'

  return (
    <div className="flex items-center gap-2.5 border-b border-edge/40 py-2 last:border-0">
      <span className={`relative flex h-2 w-2 shrink-0 ${colour}`}>
        <span className="absolute inline-flex h-full w-full rounded-full bg-current opacity-40 blur-[3px]" />
        <span className="relative inline-flex h-2 w-2 rounded-full bg-current transition-colors duration-200" />
      </span>
      <span className="flex-1 truncate text-xs text-slate-400">{label}</span>
      <span
        className={`readout text-[11px] font-semibold tracking-wide ${unknown ? 'text-slate-600' : colour}`}
      >
        {unknown ? '—' : status ? trueText : falseText}
      </span>
    </div>
  )
}
