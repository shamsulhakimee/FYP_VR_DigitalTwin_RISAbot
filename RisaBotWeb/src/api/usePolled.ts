import { useEffect, useRef, useState } from 'react'

export interface Polled<T> {
  data: T | null
  error: string | null
  /** True once a request has failed since the last success. */
  offline: boolean
}

/**
 * Polls `fetcher` on an interval. Schedules the next run only after the previous
 * one settles, so a slow robot backs the cadence off instead of queueing.
 */
export function usePolled<T>(
  fetcher: () => Promise<T>,
  intervalMs: number,
  enabled = true,
): Polled<T> {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<string | null>(null)
  const fetcherRef = useRef(fetcher)
  fetcherRef.current = fetcher

  useEffect(() => {
    if (!enabled) return
    let alive = true
    let timer: ReturnType<typeof setTimeout>

    const tick = async () => {
      try {
        const next = await fetcherRef.current()
        if (!alive) return
        setData(next)
        setError(null)
      } catch (e) {
        if (!alive) return
        setError(e instanceof Error ? e.message : String(e))
      }
      if (alive) timer = setTimeout(tick, intervalMs)
    }

    tick()
    return () => {
      alive = false
      clearTimeout(timer)
    }
  }, [intervalMs, enabled])

  return { data, error, offline: error !== null }
}
