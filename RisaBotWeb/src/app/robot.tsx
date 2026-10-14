import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import type { RobotClient } from '../api/client'
import { createClient } from '../api/demo'
import { usePolled } from '../api/usePolled'
import type { PairedRobot, RobotStatus, Telemetry } from '../api/types'

export const ROUTES = [
  'splash',
  'discovery',
  'wifi',
  'dashboard',
  'controls',
  'logs',
  'logger',
  'settings',
] as const

export type Route = (typeof ROUTES)[number]

const PAIRED_KEY = 'risabot.paired'

function loadPaired(): PairedRobot | null {
  try {
    const raw = localStorage.getItem(PAIRED_KEY)
    return raw ? (JSON.parse(raw) as PairedRobot) : null
  } catch {
    return null
  }
}

interface RobotContextValue {
  route: Route
  go: (route: Route) => void
  host: string
  client: RobotClient
  paired: PairedRobot | null
  pair: (robot: PairedRobot) => void
  forget: () => void
  telemetry: Telemetry | null
  telemetryError: string | null
  status: RobotStatus | null
  online: boolean
}

const RobotContext = createContext<RobotContextValue | null>(null)

export function RobotProvider({ children }: { children: ReactNode }) {
  const [route, setRoute] = useState<Route>('splash')
  const [paired, setPaired] = useState<PairedRobot | null>(loadPaired)
  const [host, setHost] = useState(() => loadPaired()?.host ?? '')

  const client = useMemo(() => createClient(host || 'localhost'), [host])

  // Telemetry is only worth polling once a robot is chosen; the pre-connect
  // screens would otherwise hammer a host that is still an empty string.
  const connected = host !== '' && route !== 'splash' && route !== 'discovery'

  const { data: telemetry, error: telemetryError } = usePolled(
    () => client.getTelemetry(),
    500,
    connected,
  )
  const { data: status } = usePolled(() => client.getStatus(), 3000, connected)

  const pair = useCallback((robot: PairedRobot) => {
    localStorage.setItem(PAIRED_KEY, JSON.stringify(robot))
    setPaired(robot)
    setHost(robot.host)
  }, [])

  const forget = useCallback(() => {
    localStorage.removeItem(PAIRED_KEY)
    setPaired(null)
    setHost('')
    setRoute('discovery')
  }, [])

  const value: RobotContextValue = {
    route,
    go: setRoute,
    host,
    client,
    paired,
    pair,
    forget,
    telemetry,
    telemetryError,
    status,
    online: telemetryError === null && telemetry !== null,
  }

  return <RobotContext.Provider value={value}>{children}</RobotContext.Provider>
}

export function useRobot() {
  const ctx = useContext(RobotContext)
  if (!ctx) throw new Error('useRobot must be used inside <RobotProvider>')
  return ctx
}
