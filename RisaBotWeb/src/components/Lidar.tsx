import { useEffect, useRef } from 'react'
import type { LidarData } from '../api/types'

const HALF_EXTENT_M = 1.0
const RINGS_M = [0.25, 0.5, 0.75, 1.0]

/** Matches LidarView.cs: near is dangerous, far is fine. */
function pointColour(dist: number) {
  if (dist < 0.3) return '#f87171'
  if (dist < 0.5) return '#fbbf24'
  return '#34d399'
}

/**
 * Top-down scan plot. The robot frame is x-forward / y-left, so forward maps to
 * screen-up and +y maps to screen-left.
 */
export function Lidar({ data }: { data: LidarData | null }) {
  const ref = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const canvas = ref.current
    if (!canvas) return
    const ctx = canvas.getContext('2d')
    if (!ctx) return

    const dpr = window.devicePixelRatio || 1
    const { width: cssW, height: cssH } = canvas.getBoundingClientRect()
    if (canvas.width !== cssW * dpr || canvas.height !== cssH * dpr) {
      canvas.width = cssW * dpr
      canvas.height = cssH * dpr
    }
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
    ctx.clearRect(0, 0, cssW, cssH)

    const cx = cssW / 2
    const cy = cssH / 2
    const scale = Math.min(cssW, cssH) / 2 / HALF_EXTENT_M

    ctx.strokeStyle = '#223040'
    ctx.lineWidth = 1
    for (const r of RINGS_M) {
      ctx.beginPath()
      ctx.arc(cx, cy, r * scale, 0, Math.PI * 2)
      ctx.stroke()
    }

    ctx.beginPath()
    ctx.setLineDash([4, 4])
    ctx.moveTo(cx, cy)
    ctx.lineTo(cx, cy - HALF_EXTENT_M * scale)
    ctx.strokeStyle = '#334155'
    ctx.stroke()
    ctx.setLineDash([])

    for (const p of data?.points ?? []) {
      const dist = Math.hypot(p.x, p.y)
      ctx.fillStyle = pointColour(dist)
      ctx.beginPath()
      ctx.arc(cx - p.y * scale, cy - p.x * scale, 1.8, 0, Math.PI * 2)
      ctx.fill()
    }

    ctx.fillStyle = '#38bdf8'
    ctx.beginPath()
    ctx.arc(cx, cy, 3.5, 0, Math.PI * 2)
    ctx.fill()
  }, [data])

  return (
    <div className="relative aspect-square w-full overflow-hidden rounded-xl border border-edge bg-surface">
      <canvas ref={ref} className="h-full w-full" />
      <div className="pointer-events-none absolute left-2 top-2 text-[10px] uppercase tracking-widest text-slate-500">
        {data ? `${data.points.length} pts · 1 m` : 'no scan'}
      </div>
      {data?.tunnel && (
        <div className="pointer-events-none absolute right-2 top-2 rounded-full border border-warn/40 bg-warn/15 px-2 py-0.5 text-[10px] uppercase tracking-wider text-warn">
          tunnel
        </div>
      )}
    </div>
  )
}
