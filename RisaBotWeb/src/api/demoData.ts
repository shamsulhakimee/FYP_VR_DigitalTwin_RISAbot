/**
 * The mock robot, in the browser.
 *
 * `mock/robot.mjs` serves this same competition replay over HTTP for local
 * development. A hosted build cannot use it — the page is HTTPS and every robot
 * URL is plain `http://host:8080`, which browsers block as mixed content — so the
 * generators are duplicated here and driven directly by `DemoClient`.
 *
 * Kept deliberately in step with the mock: same state sequence, same field names,
 * same shapes. If one changes, change both.
 */
import type {
  LidarData,
  LogsResponse,
  ModelInfo,
  RobotInfo,
  Telemetry,
  WifiNetwork,
} from './types'

const SEQUENCE: [string, number][] = [
  ['LANE_FOLLOW', 6],
  ['TRAFFIC_LIGHT', 3],
  ['LANE_FOLLOW', 5],
  ['BOOM_GATE', 3],
  ['LANE_FOLLOW', 4],
  ['OBSTRUCTION', 4],
  ['LANE_FOLLOW', 3],
  ['TUNNEL', 5],
  ['LANE_FOLLOW', 4],
  ['ROUNDABOUT', 4],
  ['LANE_FOLLOW', 3],
  ['HILL', 4],
  ['HILL_DESCENT', 3],
  ['LANE_FOLLOW', 4],
  ['PARALLEL_PARK', 5],
  ['FINISHED', 4],
]

const CYCLE = SEQUENCE.reduce((a, [, d]) => a + d, 0)
const BOOT = Date.now()
const elapsed = () => (Date.now() - BOOT) / 1000

function phase() {
  let t = elapsed() % CYCLE
  for (const [state, dur] of SEQUENCE) {
    if (t < dur) return { state, stateTime: t }
    t -= dur
  }
  return { state: 'LANE_FOLLOW', stateTime: 0 }
}

const params = new Map<string, string>()

/**
 * The catalog holds ~180 parameters and the demo knows none of their real
 * defaults, so a plausible value is invented per name on first read and then
 * remembered — the point is exercising the UI, not reproducing params.yaml.
 */
export function demoParam(key: string): string {
  const existing = params.get(key)
  if (existing !== undefined) return existing
  const name = key.split('.')[1] ?? ''
  let v: string
  if (/^(use_|show_|print_|invert_|enable)/.test(name) || /_enabled$/.test(name)) {
    v = name.includes('debug') ? 'false' : 'true'
  } else if (/_px$|_size$|_width$|_frames$|_count$|scanlines|threshold/.test(name)) {
    v = String(20 + ((key.length * 7) % 180))
  } else {
    v = (0.05 + ((key.length * 13) % 90) / 100).toFixed(2)
  }
  params.set(key, v)
  return v
}

export function setDemoParam(key: string, value: string) {
  params.set(key, value)
}

export const demoParamCount = () => params.size

/** Mutable bits the UI can write back to, so buttons have visible consequences. */
export const demoState = {
  launchActive: true,
  camView: 'raw',
  logger: { is_logging: false, session_name: '', started: 0 },
}

export function demoTelemetry(): Telemetry {
  const t = elapsed()
  const { state, stateTime } = phase()
  const lap = (Math.floor(t / CYCLE) % 2) + 1
  const moving = !['TRAFFIC_LIGHT', 'BOOM_GATE', 'FINISHED'].includes(state)

  const laneError = moving ? Math.sin(t * 1.7) * 22 + Math.sin(t * 0.4) * 6 : 0
  const speed = moving ? 0.32 + Math.sin(t * 0.9) * 0.06 : 0
  const distance = (t % CYCLE) * 0.31

  return {
    state,
    lap,
    auto_mode: state !== 'MANUAL',
    state_time: Math.round(stateTime),
    state_dist: (stateTime * 0.31).toFixed(2),
    traffic_light: state === 'TRAFFIC_LIGHT' ? (stateTime > 2 ? 'green' : 'red') : 'unknown',
    stop_reason:
      state === 'TRAFFIC_LIGHT' ? 'Red light' : state === 'BOOM_GATE' ? 'Gate closed' : '',

    lidar_obstacle: state === 'OBSTRUCTION',
    camera_obstacle: state === 'OBSTRUCTION' && stateTime > 1,
    fused_obstacle: state === 'OBSTRUCTION',
    boom_gate: state === 'BOOM_GATE',
    tunnel_detected: state === 'TUNNEL',
    obstruction_active: state === 'OBSTRUCTION',
    parking_complete: state === 'FINISHED',
    parking_sign_detected: state === 'PARALLEL_PARK',

    lane_error: +laneError.toFixed(2),
    cmd_lin_x: +speed.toFixed(3),
    cmd_ang_z: +(-laneError / 90).toFixed(3),
    distance: +distance.toFixed(2),
    speed: +speed.toFixed(3),
    odom_x: +(Math.cos(t * 0.25) * 2.4).toFixed(3),
    odom_y: +(Math.sin(t * 0.25) * 1.7).toFixed(3),
    odom_yaw: +(((t * 0.25) % (Math.PI * 2)) - Math.PI).toFixed(3),

    imu_roll: +(Math.sin(t * 1.1) * 2).toFixed(2),
    imu_pitch: +(state === 'HILL' ? 12 : state === 'HILL_DESCENT' ? -11 : 0.5).toFixed(2),
    imu_yaw: +(((t * 14) % 360) - 180).toFixed(2),

    buttons: Array(12).fill(0),
    axes: [0, 0, 0, 0],
    speed_pct: 35,
    ctrl_state_index: 8,
    ctrl_state_name: state,

    health_ok: true,
    health_summary: 'All nodes nominal',
    health_stale: [],
    cmd_safety_estop: false,
    cmd_safety_timeout_count: 0,
    loop_stats: { auto_driver_hz: 50.0, line_follower_hz: 31.2 },

    rp_state: 'IDLE',
    rp_buffer_size: 0,
    rp_playback_index: 0,
    rp_recording_name: '',
    rp_active_parking: 'parallel_a',
    rp_saved_recordings: ['parallel_a', 'perpendicular_b'],

    freshness_sec: { auto_mode: 0.1, lane_error: 0.03, '/scan': 0.09 },
    stale_streams: [],
    logger_status: {
      is_logging: demoState.logger.is_logging,
      session_name: demoState.logger.session_name,
      log_dir: '/home/sunrise/risabot_logs',
      duration_sec: demoState.logger.is_logging
        ? +((Date.now() - demoState.logger.started) / 1000).toFixed(1)
        : 0,
      sample_count: demoState.logger.is_logging
        ? Math.floor((Date.now() - demoState.logger.started) / 100)
        : 0,
    },
  }
}

export function demoLidar(): LidarData {
  const t = elapsed()
  const { state } = phase()
  const points: { x: number; y: number }[] = []
  const corridor = state === 'TUNNEL' ? 0.45 : 0.95

  for (let i = 0; i < 320; i++) {
    const angle = (i / 320) * Math.PI * 2 - Math.PI
    const lateral = Math.abs(Math.sin(angle))
    let r = corridor / Math.max(lateral, 0.08) + Math.sin(angle * 5 + t) * 0.04
    if (state === 'OBSTRUCTION' && angle > -0.4 && angle < 0.4) r = Math.min(r, 0.5)
    if (r > 2.0 || !Number.isFinite(r)) continue
    points.push({ x: +(r * Math.cos(angle)).toFixed(3), y: +(r * Math.sin(angle)).toFixed(3) })
  }

  return {
    points,
    tunnel: state === 'TUNNEL',
    left_dist: corridor,
    right_dist: corridor,
    dist_error: +(Math.sin(t) * 0.05).toFixed(3),
    angular_z: 0,
    centerline: [],
  }
}

export const DEMO_NETWORKS: WifiNetwork[] = [
  { ssid: 'MockNet', signal: 88, security: 'WPA2' },
  { ssid: 'RISA-Lab-5G', signal: 74, security: 'WPA2' },
  { ssid: 'Competition-Hall', signal: 61, security: 'WPA2' },
  { ssid: 'Guest', signal: 43, security: 'Open' },
  { ssid: 'eduroam', signal: 22, security: 'WPA2-EAP' },
]

export const DEMO_ROBOT_INFO: RobotInfo = {
  name: 'RISA-Bot 10 (demo)',
  serial: '10',
  model: 'Horizon RDK X5',
  firmware_version: '1.2.3',
  hostname: 'risabot10',
  mdns_hostname: 'risabot10.local',
}

export function demoLogs(): LogsResponse {
  return {
    has_error: false,
    error_count: 0,
    errors: [],
    logs: [
      '[auto_driver] Auto Driver Node Starting (Competition Mode)...',
      '[line_follower_camera] lane_error publishing at 31.2 Hz',
      '[ros2go2rtc_bridge] serving MJPEG on http://0.0.0.0:1985/mjpeg',
      `[auto_driver] state -> ${phase().state}`,
    ],
    status_summary: 'No errors found in RISA-Bot service logs.',
  }
}

export const demoModel = (): ModelInfo => ({
  present: true,
  path: '/home/sunrise/risabot_signs_640x640_nv12.bin',
  name: 'risabot_signs_640x640_nv12.bin',
  size: 7_842_304,
  mtime_iso: '2026-07-29 10:04:11',
  sha256: 'd41f8cd98f00b204e9800998ecf8427ec3a1b9f2e7d6c5048a1b2c3d4e5f6071',
  launch_active: demoState.launchActive,
  backup: { present: false },
})

const VIEW_TINT: Record<string, string> = {
  raw: '#4b5563',
  line_follower: '#0ea5e9',
  obstacle: '#f59e0b',
  signage: '#a855f7',
  traffic_light: '#ef4444',
}

/**
 * The robot serves multipart JPEG. An animated SVG is not MJPEG, but it renders
 * in an <img> the same way, so the camera component lays out for real. Returned
 * as a data URI because there is no server to fetch it from.
 */
export function demoCameraDataUri(view: string): string {
  const tint = VIEW_TINT[view] ?? '#4b5563'
  const dashes = Array.from({ length: 6 }, (_, i) => {
    const y = i * 60
    return `<rect x="152" y="${y}" width="16" height="34" rx="4" fill="#e5e7eb" opacity="0.85"><animate attributeName="y" from="${y}" to="${y + 60}" dur="0.7s" repeatCount="indefinite"/></rect>`
  }).join('')

  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="320" height="240" viewBox="0 0 320 240"><rect width="320" height="240" fill="#111827"/><polygon points="160,60 40,240 280,240" fill="#1f2937"/><polygon points="160,60 96,240 120,240" fill="${tint}" opacity="0.25"/><polygon points="160,60 200,240 224,240" fill="${tint}" opacity="0.25"/>${dashes}<rect x="0" y="0" width="320" height="22" fill="#000" opacity="0.55"/><text x="8" y="15" font-family="monospace" font-size="12" fill="${tint}">${view}</text><text x="312" y="232" font-family="monospace" font-size="10" fill="#6b7280" text-anchor="end">DEMO CAMERA</text></svg>`

  return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`
}

export const DEMO_PROVISION_STEPS: [string, string, number][] = [
  ['arp', 'associating with access point', 700],
  ['dhcp', 'requesting an address', 1100],
  ['internet_probe', 'checking internet reachability', 900],
  ['ros_check', 'waiting for ROS nodes', 1200],
]
