import { useEffect } from 'react'
import { useRobot } from '../app/robot'
import { IconChip } from '../components/icons'

export function Splash() {
  const { go } = useRobot()

  // Always lands on Discovery even with a robot remembered: a saved pairing is a
  // default, not a decision, and several robots may be on the bench.
  useEffect(() => {
    const t = setTimeout(() => go('discovery'), 900)
    return () => clearTimeout(t)
  }, [go])

  return (
    <div className="flex h-full flex-col items-center justify-center gap-5">
      <div className="relative h-20 w-20">
        <div className="absolute inset-0 animate-ping rounded-3xl bg-accent/15" />
        <div className="raised absolute inset-0 flex items-center justify-center rounded-3xl border border-accent/40 bg-accent/10 text-3xl text-accent shadow-[0_0_40px] shadow-accent/20">
          <IconChip />
        </div>
      </div>
      <div className="text-center">
        <h1 className="text-xl font-semibold tracking-tight">RISA-Bot</h1>
        <p className="mt-1 text-[10px] uppercase tracking-[0.35em] text-slate-500">Field Console</p>
      </div>
      <div className="h-0.5 w-24 overflow-hidden rounded-full bg-edge">
        <div className="h-full w-1/3 animate-[ping_1.2s_ease-in-out_infinite] rounded-full bg-accent" />
      </div>
    </div>
  )
}
