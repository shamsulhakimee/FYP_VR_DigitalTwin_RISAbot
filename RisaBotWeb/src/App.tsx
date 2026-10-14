import type { ComponentType } from 'react'
import { DEMO, MIXED_CONTENT_BLOCKED, ROBOT_APP_URL } from './api/demo'
import { RobotProvider, useRobot, type Route } from './app/robot'
import { isMaskedAddress } from './components/mask'
import {
  IconActivity,
  IconChip,
  IconList,
  IconRecord,
  IconSliders,
} from './components/icons'
import { Controls } from './routes/Controls'
import { Dashboard } from './routes/Dashboard'
import { DataLogger } from './routes/DataLogger'
import { Discovery } from './routes/Discovery'
import { Logs } from './routes/Logs'
import { Settings } from './routes/Settings'
import { Splash } from './routes/Splash'
import { WifiSetup } from './routes/WifiSetup'

/** Routes reachable from the tab bar; the rest are part of the connect flow. */
const TABS: { route: Route; label: string; Icon: ComponentType<{ className?: string }> }[] = [
  { route: 'dashboard', label: 'Live', Icon: IconActivity },
  { route: 'controls', label: 'Control', Icon: IconSliders },
  { route: 'logger', label: 'Record', Icon: IconRecord },
  { route: 'logs', label: 'Logs', Icon: IconList },
  { route: 'settings', label: 'Robot', Icon: IconChip },
]

const CHROMELESS: Route[] = ['splash', 'discovery', 'wifi']

function Screen() {
  const { route } = useRobot()
  switch (route) {
    case 'splash':
      return <Splash />
    case 'discovery':
      return <Discovery />
    case 'wifi':
      return <WifiSetup />
    case 'dashboard':
      return <Dashboard />
    case 'controls':
      return <Controls />
    case 'logs':
      return <Logs />
    case 'logger':
      return <DataLogger />
    case 'settings':
      return <Settings />
  }
}

function Shell() {
  const { route, go, paired, host, online, status } = useRobot()
  const chromeless = CHROMELESS.includes(route)

  return (
    <div className="mx-auto flex h-full max-w-md flex-col">
      {/* Shown on every route, including the chromeless ones — the hosted build
          opens on Splash, and that is exactly where it must not look live. */}
      {DEMO && (
        <div className="shrink-0 bg-warn/15 px-3 py-1.5 text-[10px] leading-relaxed text-warn/90">
          <span className="mr-1.5 rounded-full border border-warn/50 px-1.5 py-px font-semibold uppercase tracking-[0.12em] text-warn">
            demo
          </span>
          Simulated robot, no hardware. The robot serves the real app itself — join its WiFi
          hotspot and open <span className="readout text-warn">{ROBOT_APP_URL}</span>
        </div>
      )}

      {/* Real mode was asked for from an HTTPS page. Every call is about to fail
          as a bare "Failed to fetch", which looks identical to a robot that is
          switched off — so name the real cause before the first timeout does. */}
      {MIXED_CONTENT_BLOCKED && (
        <div className="shrink-0 bg-bad/15 px-3 py-2 text-[10px] leading-relaxed text-bad">
          <span className="mr-1.5 rounded-full border border-bad/50 px-1.5 py-px font-semibold uppercase tracking-[0.12em]">
            blocked
          </span>
          This page is HTTPS and the robot is plain HTTP, so the browser refuses every request
          before it is sent. No setting here changes that — join the robot's WiFi hotspot and
          open <span className="readout text-bad">{ROBOT_APP_URL}</span>, which is the same app
          served by the robot.
        </div>
      )}

      {!chromeless && (
        <header className="flex shrink-0 items-center justify-between gap-3 border-b border-edge/70 bg-surface/70 px-4 py-3 backdrop-blur">
          <button
            onClick={() => go('discovery')}
            className="flex min-w-0 items-center gap-2.5 text-left transition active:opacity-70"
          >
            <span
              className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border text-lg ${
                online
                  ? 'border-accent/30 bg-accent/10 text-accent'
                  : 'border-edge bg-panel text-slate-600'
              }`}
            >
              <IconChip />
            </span>
            <span className="min-w-0">
              <h1 className="truncate text-sm font-semibold leading-tight">
                {paired?.name ?? 'RISA-Bot'}
              </h1>
              {/* Deliberately not a routable address. This sits on screen for the
                  whole session, so it is the single most likely thing to end up
                  in a screenshot or on a projector. A name identifies the robot
                  just as well; the address is under Robot > Address.
                  The hotspot gateway is exempt and shows in full — it is the
                  same on every robot and the phone already displays it. */}
              <p className="readout truncate text-[11px] leading-tight text-slate-500">
                {isMaskedAddress(host) ? (paired?.mdns ?? 'paired') : host}
              </p>
            </span>
          </button>
          <div className="flex shrink-0 items-center gap-1.5">
            {status?.launch_active && (
              <span className="rounded-full border border-accent/40 bg-accent/15 px-2 py-1 text-[9px] font-semibold uppercase tracking-[0.12em] text-accent">
                ros
              </span>
            )}
            <span
              className={`flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[9px] font-semibold uppercase tracking-[0.12em] ${
                online ? 'border-good/40 bg-good/15 text-good' : 'border-bad/40 bg-bad/15 text-bad'
              }`}
            >
              <span className="relative flex h-1.5 w-1.5">
                {online && (
                  <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-current opacity-75" />
                )}
                <span className="relative inline-flex h-1.5 w-1.5 rounded-full bg-current" />
              </span>
              {online ? 'live' : 'offline'}
            </span>
          </div>
        </header>
      )}

      <main className="min-h-0 flex-1 overflow-y-auto p-4">
        <Screen />
      </main>

      {!chromeless && (
        <nav className="flex shrink-0 border-t border-edge/70 bg-panel/85 pb-[env(safe-area-inset-bottom)] backdrop-blur">
          {TABS.map(({ route: r, label, Icon }) => {
            const active = route === r
            return (
              <button
                key={r}
                onClick={() => go(r)}
                className={`relative flex flex-1 flex-col items-center gap-1 py-2.5 transition duration-150 active:scale-95 ${
                  active ? 'text-accent' : 'text-slate-500'
                }`}
              >
                {/* The lit rail is what marks the tab; colour alone was too quiet. */}
                <span
                  className={`absolute top-0 h-0.5 w-8 rounded-full bg-accent transition-opacity duration-200 ${
                    active ? 'opacity-100 shadow-[0_0_8px] shadow-accent/60' : 'opacity-0'
                  }`}
                />
                <Icon className="text-[19px]" />
                <span className="text-[10px] font-medium tracking-wide">{label}</span>
              </button>
            )
          })}
        </nav>
      )}
    </div>
  )
}

export default function App() {
  return (
    <RobotProvider>
      <Shell />
    </RobotProvider>
  )
}
