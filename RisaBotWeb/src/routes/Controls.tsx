import { useCallback, useEffect, useRef, useState } from 'react'
import { PARAM_GROUPS, type ParamGroup } from '../api/paramCatalog'
import { useRobot } from '../app/robot'
import { IconChevronRight, IconPower, IconRefresh } from '../components/icons'
import { Badge, Btn, Card, PageHeader } from '../components/ui'

function matching(group: ParamGroup, q: string) {
  if (!q) return group.params
  if (group.label.toLowerCase().includes(q) || group.node.includes(q)) return group.params
  return group.params.filter(
    (p) => p.name.toLowerCase().includes(q) || p.tip.toLowerCase().includes(q),
  )
}

function Parameters() {
  const { client } = useRobot()
  const [values, setValues] = useState<Record<string, string>>({})
  const [drafts, setDrafts] = useState<Record<string, string>>({})
  const [open, setOpen] = useState<Set<string>>(new Set())
  const [loading, setLoading] = useState<Set<string>>(new Set())
  const [filter, setFilter] = useState('')
  const [busy, setBusy] = useState('')
  const [note, setNote] = useState('')
  // A group is fetched once and then kept; the ref (not state) is what guards
  // against a second fetch, since the effect below runs before state commits.
  const fetched = useRef<Set<string>>(new Set())

  const loadGroup = useCallback(
    async (group: ParamGroup) => {
      fetched.current.add(group.label)
      setLoading((s) => new Set(s).add(group.label))
      const next: Record<string, string> = {}
      await Promise.all(
        group.params.map(async (p) => {
          try {
            const res = await client.getParam(group.node, p.name)
            if (res.ok && res.value !== undefined) next[`${group.node}.${p.name}`] = res.value
          } catch {
            /* a node that isn't running just leaves the row blank */
          }
        }),
      )
      setValues((v) => ({ ...v, ...next }))
      setDrafts((d) => ({ ...d, ...next }))
      setLoading((s) => {
        const n = new Set(s)
        n.delete(group.label)
        return n
      })
    },
    [client],
  )

  const q = filter.trim().toLowerCase()

  // Filtering reveals rows the user has not expanded, so their values are fetched too.
  useEffect(() => {
    if (!q) return
    for (const g of PARAM_GROUPS) {
      if (matching(g, q).length && !fetched.current.has(g.label)) void loadGroup(g)
    }
  }, [q, loadGroup])

  const toggle = (group: ParamGroup) => {
    setOpen((s) => {
      const n = new Set(s)
      if (n.has(group.label)) n.delete(group.label)
      else n.add(group.label)
      return n
    })
    if (!fetched.current.has(group.label)) void loadGroup(group)
  }

  const reload = () => {
    const again = [...fetched.current]
    fetched.current = new Set()
    for (const g of PARAM_GROUPS) if (again.includes(g.label)) void loadGroup(g)
  }

  const apply = async (node: string, param: string) => {
    const key = `${node}.${param}`
    setBusy(key)
    setNote('')
    try {
      const res = await client.setParam(node, param, drafts[key] ?? '')
      if (res.ok) {
        setValues((v) => ({ ...v, [key]: drafts[key] }))
        setNote(`${param} set to ${drafts[key]}`)
      } else {
        setNote(res.error ?? 'Rejected')
      }
    } catch (e) {
      setNote(e instanceof Error ? e.message : String(e))
    }
    setBusy('')
  }

  const visible = PARAM_GROUPS.map((g) => [g, matching(g, q)] as const).filter(
    ([, params]) => params.length > 0,
  )

  return (
    <Card
      title="Live tuning"
      right={
        <button
          onClick={reload}
          className="text-[11px] text-accent underline underline-offset-4"
        >
          reload
        </button>
      }
    >
      <input
        value={filter}
        onChange={(e) => setFilter(e.target.value)}
        placeholder="Filter parameters"
        className="mb-3 w-full rounded-lg border border-edge bg-surface px-3 py-2 text-sm text-slate-100 outline-none transition-colors placeholder:text-slate-600 focus:border-accent"
      />

      <div className="flex flex-col gap-1.5">
        {visible.map(([group, params]) => {
          const expanded = open.has(group.label) || q !== ''
          return (
            <div key={group.label} className="rounded-xl border border-edge/70 bg-surface/40">
              <button
                onClick={() => toggle(group)}
                className="flex w-full items-center gap-2 px-3 py-2.5 text-left"
              >
                <IconChevronRight
                  className={`shrink-0 text-[14px] text-slate-500 transition-transform duration-150 ${
                    expanded ? 'rotate-90' : ''
                  }`}
                />
                <span className="min-w-0 flex-1 truncate text-xs font-semibold text-slate-200">
                  {group.label}
                </span>
                <span className="shrink-0 text-[10px] text-slate-600">
                  {loading.has(group.label) ? 'loading…' : params.length}
                </span>
              </button>

              {expanded && (
                <div className="flex flex-col gap-3 border-t border-edge/60 px-3 pb-3 pt-3">
                  <p className="-mt-1 font-mono text-[10px] text-slate-600">{group.node}</p>
                  {params.map((p) => {
                    const key = `${group.node}.${p.name}`
                    const dirty = drafts[key] !== values[key]
                    return (
                      <div key={key}>
                        <div className="mb-1">
                          <span className="text-xs text-slate-300">{p.name}</span>
                          <p className="text-[10px] leading-snug text-slate-500">{p.tip}</p>
                        </div>
                        <div className="flex gap-2">
                          <input
                            value={drafts[key] ?? ''}
                            onChange={(e) =>
                              setDrafts((d) => ({ ...d, [key]: e.target.value }))
                            }
                            onKeyDown={(e) =>
                              e.key === 'Enter' && void apply(group.node, p.name)
                            }
                            placeholder="—"
                            className={`readout min-w-0 flex-1 rounded-lg border bg-surface px-3 py-2 text-sm font-semibold text-slate-100 outline-none transition-colors ${
                              dirty ? 'border-warn/50 bg-warn/5' : 'border-edge'
                            } focus:border-accent`}
                          />
                          <Btn
                            tone={dirty ? 'warn' : 'default'}
                            disabled={!dirty || busy === key}
                            onClick={() => void apply(group.node, p.name)}
                            className="!px-3 !py-2"
                          >
                            Apply
                          </Btn>
                        </div>
                      </div>
                    )
                  })}
                </div>
              )}
            </div>
          )
        })}
        {visible.length === 0 && (
          <p className="py-4 text-center text-xs text-slate-500">No parameter matches “{filter}”.</p>
        )}
      </div>

      {note && <p className="mt-3 text-[11px] text-slate-400">{note}</p>}

      <Btn
        className="mt-3 w-full"
        onClick={async () => {
          setBusy('save')
          try {
            const res = await client.saveDefaults()
            setNote(res.msg ?? 'Saved to params.yaml')
          } catch (e) {
            setNote(e instanceof Error ? e.message : String(e))
          }
          setBusy('')
        }}
        disabled={busy === 'save'}
      >
        {busy === 'save' ? 'Saving…' : 'Save as defaults'}
      </Btn>
    </Card>
  )
}

export function Controls() {
  const { client, status, telemetry } = useRobot()
  const [busy, setBusy] = useState('')
  const [note, setNote] = useState('')
  const [error, setError] = useState('')

  const launchActive = Boolean(status?.launch_active)

  const run = async (name: string, fn: () => Promise<{ ok?: boolean; msg?: string }>) => {
    setBusy(name)
    setError('')
    setNote('')
    try {
      const res = await fn()
      setNote(res.msg ?? `${name} sent`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
    setBusy('')
  }

  return (
    <div className="flex flex-col gap-3">
      <PageHeader title="Controls" subtitle="Launch, competition commands and calibration." />

      {error && (
        <div className="rounded-xl border border-bad/40 bg-bad/10 p-3 text-xs text-bad">{error}</div>
      )}
      {note && !error && (
        <div className="rounded-xl border border-edge bg-panel p-3 text-xs text-slate-400">{note}</div>
      )}

      <Card
        title="System"
        right={
          <Badge tone={launchActive ? 'good' : 'default'}>
            {launchActive ? 'running' : 'stopped'}
          </Badge>
        }
      >
        <p className="mb-3 text-xs leading-relaxed text-slate-500">
          Starts risabot.service on the robot. Nodes take a few seconds to appear after systemd
          reports active.
        </p>
        <Btn
          tone={launchActive ? 'bad' : 'good'}
          className="w-full"
          disabled={busy === 'launch'}
          onClick={() =>
            run('launch', () => (launchActive ? client.launchStop() : client.launchStart()))
          }
        >
          <IconPower className="text-[16px]" />
          {busy === 'launch'
            ? 'Working…'
            : launchActive
              ? 'Stop system'
              : 'Start system'}
        </Btn>
      </Card>

      <Card title={`Run · lap ${telemetry?.lap ?? '—'}`}>
        <div className="grid grid-cols-3 gap-2">
          <Btn disabled={busy === 'RESET'} onClick={() => run('RESET', () => client.resetCompetition('RESET'))}>
            Reset
          </Btn>
          <Btn disabled={busy === 'LAP1'} onClick={() => run('LAP1', () => client.resetCompetition('LAP1'))}>
            Lap 1
          </Btn>
          <Btn disabled={busy === 'LAP2'} onClick={() => run('LAP2', () => client.resetCompetition('LAP2'))}>
            Lap 2
          </Btn>
        </div>
      </Card>

      <Card title="Calibration">
        <div className="grid grid-cols-2 gap-2">
          <Btn disabled={busy === 'odom'} onClick={() => run('odom', () => client.resetOdom())}>
            Reset odometry
          </Btn>
          <Btn disabled={busy === 'imu'} onClick={() => run('imu', () => client.calibrateImu())}>
            Calibrate IMU
          </Btn>
        </div>
      </Card>

      <Parameters />

      <Card title="Service">
        <p className="mb-3 text-xs leading-relaxed text-slate-500">
          Restarting reloads the AI model and every node. Takes 60–90 seconds.
        </p>
        <Btn
          tone="warn"
          className="w-full"
          disabled={busy === 'restart'}
          onClick={() =>
            run('restart', async () => {
              const res = await client.restartRos()
              return { msg: `ROS service is ${res.status.trim()}` }
            })
          }
        >
          <IconRefresh className={`text-[15px] ${busy === 'restart' ? 'animate-spin' : ''}`} />
          {busy === 'restart' ? 'Restarting…' : 'Restart ROS'}
        </Btn>
      </Card>
    </div>
  )
}
