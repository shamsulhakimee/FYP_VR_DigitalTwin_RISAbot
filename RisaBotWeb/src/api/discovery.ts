import { RobotClient } from './client'
import { DEMO } from './demo'
import { DEMO_ROBOT_INFO } from './demoData'
import { HOTSPOT_IP } from './net'
import type { DiscoveredRobot, PairedRobot } from './types'

const PROBE_TIMEOUT_MS = 2500

/**
 * The sweep is 254 addresses, so its timeout has to be short enough that a whole
 * subnet finishes in a sane time and long enough that a busy robot still answers.
 */
const SWEEP_TIMEOUT_MS = 1500
const SWEEP_CONCURRENCY = 24

/**
 * The WPF app probes a fixed list of hostnames and keeps whatever answers. We
 * probe the same list, but it is close to useless in a browser: 11 of those 13
 * names are mDNS, and `risabotN.local` resolves to AAAA records the robot never
 * serves (services bind 0.0.0.0). Unlike a socket, `fetch()` gives no way to ask
 * for A records only, so those probes blackhole until they time out.
 *
 * Hence these are the names worth trying, and the /24 sweep below is what
 * actually finds a robot on a normal LAN.
 */
function namedHosts(paired: PairedRobot | null): string[] {
  const hosts = [HOTSPOT_IP, 'risabot.local']
  for (let i = 1; i <= 10; i++) hosts.push(`risabot${i}.local`)

  // In dev the mock answers on the origin's own host.
  if (location.hostname) hosts.push(location.hostname)

  if (paired) {
    hosts.push(paired.host)
    if (paired.mdns) hosts.push(paired.mdns)
  }
  return [...new Set(hosts.filter(Boolean))]
}

/** `192.168.68.53` -> `192.168.68`. Anything else (a name, IPv6) -> null. */
function ipv4Prefix(host: string): string | null {
  const m = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/.exec(host)
  if (!m) return null
  const octets = m.slice(1).map(Number)
  if (octets.some((o) => o > 255)) return null
  return octets.slice(0, 3).join('.')
}

/**
 * Every address on the same /24 as somewhere we already are.
 *
 * `location.hostname` covers a browser opened on the LAN dev server. It is
 * `localhost` inside the Capacitor shell, which yields nothing — so the last
 * known address of the paired robot is used as well, since a robot that moved
 * usually moved within its own subnet.
 */
function sweepHosts(paired: PairedRobot | null, exclude: Set<string>): string[] {
  const prefixes = new Set(
    [ipv4Prefix(location.hostname), paired && ipv4Prefix(paired.host)].filter(
      (p): p is string => Boolean(p),
    ),
  )

  const hosts: string[] = []
  for (const prefix of prefixes) {
    for (let i = 1; i <= 254; i++) {
      const host = `${prefix}.${i}`
      if (!exclude.has(host)) hosts.push(host)
    }
  }
  return hosts
}

/**
 * One robot answers on several addresses and we have to persist exactly one.
 * Higher wins.
 *
 * A literal beats a name even though both just answered: mDNS is resolved by
 * whatever the host OS feels like returning, and `risabotN.local` has AAAA
 * records the robot does not listen on. The hotspot address works, but only
 * while the phone is on the robot's own AP, so a routable literal beats it.
 */
function rank(r: DiscoveredRobot): number {
  if (!ipv4Prefix(r.host)) return 0
  return r.isHotspot ? 1 : 2
}

async function probe(
  host: string,
  paired: PairedRobot | null,
  timeoutMs: number,
): Promise<DiscoveredRobot | null> {
  try {
    const info = await new RobotClient(host).getRobotInfo(timeoutMs)
    if (!info?.serial) return null
    return {
      host,
      info,
      isHotspot: host === HOTSPOT_IP,
      isPaired: paired?.serial === info.serial,
    }
  } catch {
    return null
  }
}

/** Bounded fan-out — 254 simultaneous fetches would just queue inside the browser. */
async function pooled(hosts: string[], limit: number, run: (host: string) => Promise<void>) {
  let next = 0
  await Promise.all(
    Array.from({ length: Math.min(limit, hosts.length) }, async () => {
      while (next < hosts.length) await run(hosts[next++])
    }),
  )
}

/**
 * Reports each hit as it lands, so the list fills in progressively instead of
 * waiting on the slowest timeout. Pass a signal so a rescan (or leaving the
 * page) stops the sweep issuing new requests.
 */
export async function scanForRobots(
  paired: PairedRobot | null,
  onFound?: (robot: DiscoveredRobot) => void,
  signal?: AbortSignal,
): Promise<DiscoveredRobot[]> {
  // Probing 13 hostnames plus a subnet that cannot answer would just be timeouts.
  if (DEMO) {
    // A plausible LAN address rather than "demo": nothing routes anywhere, but
    // the address is shown all over the UI and should read like the real thing.
    const found: DiscoveredRobot = {
      host: '10.0.0.42',
      info: DEMO_ROBOT_INFO,
      isHotspot: false,
      isPaired: paired?.serial === DEMO_ROBOT_INFO.serial,
    }
    await new Promise((r) => setTimeout(r, 600))
    onFound?.(found)
    return [found]
  }

  const bySerial = new Map<string, DiscoveredRobot>()
  let settled = false

  const keep = (found: DiscoveredRobot | null) => {
    if (!found || settled) return
    const existing = bySerial.get(found.info.serial)
    if (existing && rank(found) <= rank(existing)) return
    bySerial.set(found.info.serial, found)
    onFound?.(found)
  }

  const named = namedHosts(paired)

  // The hotspot gets its own probe, raced against everything else, because it is
  // the supported way to reach a robot and it should not wait behind a sweep.
  const hotspotScan = probe(HOTSPOT_IP, paired, PROBE_TIMEOUT_MS)

  // Everything else. The named probes run at once — there are only a handful,
  // and the mDNS ones are dead weight that must not hold up the sweep.
  const stopped = () => settled || Boolean(signal?.aborted)
  const otherScan = Promise.all([
    Promise.all(
      named
        .filter((h) => h !== HOTSPOT_IP)
        .map(async (host) => keep(await probe(host, paired, PROBE_TIMEOUT_MS))),
    ),
    pooled(sweepHosts(paired, new Set(named)), SWEEP_CONCURRENCY, async (host) => {
      if (stopped()) return
      keep(await probe(host, paired, SWEEP_TIMEOUT_MS))
    }),
  ])

  // If the hotspot answers, the phone is on the robot's AP: a private /24 whose
  // only other member is the phone itself. Nothing the sweep is probing can
  // possibly reply, and `paired.host` is typically a LAN address from a previous
  // session, so continuing means ~254 guaranteed timeouts before a result the
  // user could already have had. Take the answer and stop.
  const hotspot = await hotspotScan
  if (hotspot) {
    keep(hotspot)
    settled = true
    void otherScan // let the in-flight probes die on their own; ignore their results
    return [...bySerial.values()]
  }

  await otherScan

  return [...bySerial.values()].sort(
    (a, b) => Number(b.isPaired) - Number(a.isPaired) || Number(b.isHotspot) - Number(a.isHotspot),
  )
}
