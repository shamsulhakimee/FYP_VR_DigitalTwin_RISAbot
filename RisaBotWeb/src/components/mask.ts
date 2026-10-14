import { HOTSPOT_IP } from '../api/net'

/**
 * The address-hiding policy, kept apart from the component that renders it.
 *
 * Separate file because this is the part with rules in it — an exemption list,
 * a regex, and a predicate two different screens have to agree on — and it is
 * checked by scripts/check-masking.mjs. Pure TypeScript, no JSX, so that check
 * needs no build step.
 */

/**
 * `192.168.4.1` -> true. A name like `risabot10.local` -> false.
 *
 * The distinction is the whole policy: a *name* says which robot, an *address*
 * says where to reach it. Names stay on screen, addresses generally do not.
 */
export function isIpLiteral(host: string): boolean {
  const m = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$/.exec(host.trim())
  return Boolean(m) && m!.slice(1).every((o) => Number(o) <= 255)
}

/**
 * Addresses shown in full, because hiding them protects nothing and costs
 * clarity.
 *
 * `192.168.4.1` is the robot hotspot's *gateway*. Every phone joined to the AP
 * already shows it in its own WiFi settings, unprompted, and it is identical on
 * every RISA-Bot — so it is not a fact anyone needs this app to learn. Masking
 * it only made the one screen an operator reads while standing next to the
 * robot harder to read.
 *
 * The distinction that survives: a *routable* address says which network the
 * robot is on and how to reach it from elsewhere, which is worth keeping off a
 * projector. The hotspot gateway says nothing beyond "you are on the AP" —
 * which you knew, because you joined it.
 *
 * None of this is a security boundary; it is about screenshots and shoulders.
 */
const PUBLIC_ADDRESSES = new Set<string>([HOTSPOT_IP])

/**
 * Any dotted quad inside a longer string — `http://192.168.68.54:8080` and
 * friends. The `\b` guards matter: without them `1192.168.4.1` would match the
 * exempt address as a substring and be shown.
 */
const IPV4_ANYWHERE = /\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b/g

const DOTS = '•••.•••.•••.•••'

/** Keeps the shape of a URL so it still reads as one, minus the address. */
export function maskAddresses(text: string): string {
  return text.replace(IPV4_ANYWHERE, (addr) => (PUBLIC_ADDRESSES.has(addr) ? addr : DOTS))
}

/**
 * True when `host` is an address this app hides — a dotted quad that is not
 * exempt. Callers wanting "show a name instead of numbers" want this, not
 * `isIpLiteral`: the hotspot gateway is a literal *and* stays on screen.
 */
export function isMaskedAddress(host: string): boolean {
  const h = host.trim()
  return isIpLiteral(h) && !PUBLIC_ADDRESSES.has(h)
}
