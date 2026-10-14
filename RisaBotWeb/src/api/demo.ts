import { DemoClient } from './demoClient'
import { RobotClient } from './client'

const params = new URLSearchParams(globalThis.location?.search ?? '')

/**
 * On by default in the hosted build (VITE_DEMO=1), forceable anywhere with
 * `?demo=1`, and defeatable with `?real=1`. A local `npm run dev` leaves it off
 * so the app talks to the mock or to real hardware.
 *
 * `?real=1` exists because the demo default is a property of *where the build is
 * served from*, not of the build itself — the same bundle is real inside the
 * Capacitor shell. It is not a promise that a connection will work; see below.
 */
export const DEMO = params.has('real')
  ? false
  : import.meta.env.VITE_DEMO === '1' || params.has('demo')

/**
 * Real mode from an HTTPS page is doomed and worth saying out loud.
 *
 * Every robot URL is `http://` or `ws://`, and a browser blocks those as active
 * mixed content when the page itself is HTTPS. The failure surfaces as an opaque
 * `TypeError: Failed to fetch` with no status and no CORS message, which reads
 * exactly like the robot being switched off — so the app would otherwise blame
 * the hardware for a decision the browser made.
 *
 * The only escapes are a plain-HTTP origin — which is what the robot itself
 * serves, see ROBOT_APP_URL — or the user allowing insecure content for this
 * site in their browser settings.
 */
export const MIXED_CONTENT_BLOCKED = !DEMO && globalThis.location?.protocol === 'https:'

/**
 * Where the copy that can actually reach hardware lives.
 *
 * The robot serves this same bundle from its provisioning portal on :8000,
 * which is a plain-HTTP origin on the same network as the robot — the one place
 * the mixed-content rule above does not apply. Every dead end in the hosted
 * build points here, rather than leaving the user to conclude the app is broken
 * or the robot is off.
 *
 * The name resolves through the robot's own dnsmasq, which is the DNS server
 * handed to every client of its hotspot, so it needs no setup on the phone.
 */
export const ROBOT_APP_URL = 'risabot.internal:8000'

export function createClient(host: string): RobotClient {
  return DEMO ? new DemoClient() : new RobotClient(host)
}
