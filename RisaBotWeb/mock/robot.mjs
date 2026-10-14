/**
 * Fake RISA-bot. Serves the same JSON contract as dashboard.py (:8080) and
 * app.py (:8000) so the web app can be built without hardware.
 *
 *   node mock/robot.mjs
 *
 * Deliberately not a ROS simulator — it replays a plausible competition run so
 * every field the UI binds to has believable motion.
 */
import http from 'node:http'
import crypto from 'node:crypto'

const SEQUENCE = [
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

const params = new Map()

/**
 * The catalog holds ~180 parameters and this mock knows none of their real defaults,
 * so a plausible value is invented per name on first read and then remembered — the
 * point is exercising the UI, not reproducing params.yaml.
 */
function paramValue(key) {
  if (params.has(key)) return params.get(key)
  const name = key.split('.')[1] ?? ''
  let v
  if (/^(use_|show_|print_|invert_|enable)/.test(name) || /_enabled$/.test(name)) {
    v = name.includes('debug') ? 'false' : 'true'
  } else if (/_px$|_size$|_width$|_frames$|_count$|scanlines|threshold/.test(name)) {
    v = String(20 + (key.length * 7) % 180)
  } else {
    v = (0.05 + ((key.length * 13) % 90) / 100).toFixed(2)
  }
  params.set(key, v)
  return v
}

const logger = { is_logging: false, session_name: '', started: 0, sample_count: 0 }
let launchActive = true
let camView = 'raw'
let wifiState = 'connected'
let connectedSsid = 'MockNet'
let pendingPassword = ''

let model = {
  present: true,
  path: '/home/sunrise/risabot_signs_640x640_nv12.bin',
  name: 'risabot_signs_640x640_nv12.bin',
  size: 7_842_304,
  mtime: Date.now() / 1000 - 9 * 86400,
  mtime_iso: '2026-07-29 10:04:11',
  sha256: 'd41f8cd98f00b204e9800998ecf8427ec3a1b9f2e7d6c5048a1b2c3d4e5f6071',
}
let modelBackup = null

const elapsed = () => (Date.now() - BOOT) / 1000

function phase() {
  let t = elapsed() % CYCLE
  for (const [state, dur] of SEQUENCE) {
    if (t < dur) return { state, stateTime: t, dur }
    t -= dur
  }
  return { state: 'LANE_FOLLOW', stateTime: 0, dur: 1 }
}

function telemetry() {
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
    traffic_light:
      state === 'TRAFFIC_LIGHT' ? (stateTime > 2 ? 'green' : 'red') : 'unknown',
    stop_reason:
      state === 'TRAFFIC_LIGHT'
        ? 'Red light'
        : state === 'BOOM_GATE'
          ? 'Gate closed'
          : '',

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

    health_ok: state !== 'EMERGENCY_STOP',
    health_summary: state === 'EMERGENCY_STOP' ? 'E-stop asserted' : 'All nodes nominal',
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
      ...logger,
      duration_sec: logger.is_logging ? +((Date.now() - logger.started) / 1000).toFixed(1) : 0,
      sample_count: logger.is_logging
        ? Math.floor((Date.now() - logger.started) / 100)
        : 0,
    },
  }
}

function lidar() {
  const t = elapsed()
  const { state } = phase()
  const points = []
  const corridor = state === 'TUNNEL' ? 0.45 : 0.95

  for (let i = 0; i < 320; i++) {
    const angle = (i / 320) * Math.PI * 2 - Math.PI
    const lateral = Math.abs(Math.sin(angle))
    let r = corridor / Math.max(lateral, 0.08) + Math.sin(angle * 5 + t) * 0.04
    if (state === 'OBSTRUCTION' && angle > -0.4 && angle < 0.4) r = Math.min(r, 0.5)
    if (r > 2.0 || !Number.isFinite(r)) continue
    points.push({
      x: +(r * Math.cos(angle)).toFixed(3),
      y: +(r * Math.sin(angle)).toFixed(3),
    })
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

function send(res, status, body, type = 'application/json') {
  const payload = type === 'application/json' ? JSON.stringify(body) : body
  res.writeHead(status, {
    'Content-Type': type,
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': '*',
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
    'Cache-Control': 'no-store',
  })
  res.end(payload)
}

function readBody(req) {
  return new Promise((resolve) => {
    let raw = ''
    req.on('data', (c) => (raw += c))
    req.on('end', () => {
      try {
        resolve(raw ? JSON.parse(raw) : {})
      } catch {
        resolve({})
      }
    })
  })
}

/** The model upload is a raw binary body, so it must not go through JSON parsing. */
function readRaw(req) {
  return new Promise((resolve) => {
    const chunks = []
    req.on('data', (c) => chunks.push(c))
    req.on('end', () => resolve(Buffer.concat(chunks)))
  })
}

/* ---------------- port 8080: dashboard telemetry + control ---------------- */

const dashboard = http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://localhost')
  const p = url.pathname

  if (req.method === 'OPTIONS') return send(res, 204, '')

  if (p === '/data') return send(res, 200, telemetry())
  if (p === '/lidar_data') return send(res, 200, lidar())

  if (p === '/api/set_cam_view') {
    camView = url.searchParams.get('view') ?? 'raw'
    return send(res, 200, { ok: true })
  }

  if (p === '/api/get_param') {
    const key = `${url.searchParams.get('node')}.${url.searchParams.get('param')}`
    const value = paramValue(key)
    return send(res, 200, { ok: true, value, default: value })
  }

  if (p === '/api/logger/status') {
    return send(res, 200, { ok: true, data: telemetry().logger_status })
  }

  if (p === '/api/logger/list') {
    return send(res, 200, {
      ok: true,
      sessions: [
        { name: 'session_2026-08-06_141203', time: '2026-08-06 14:12:03', duration: 62.4, samples: 624 },
        { name: 'session_2026-08-06_152230', time: '2026-08-06 15:22:30', duration: 31.8, samples: 318 },
      ],
    })
  }

  if (req.method === 'POST') {
    const body = await readBody(req)

    if (p === '/api/set_param') {
      params.set(`${body.node}.${body.param}`, String(body.value))
      return send(res, 200, { ok: true, msg: 'OK' })
    }
    if (p === '/api/save_defaults') {
      return send(res, 200, { ok: true, msg: `Saved ${params.size} params`, updated: params.size })
    }
    if (p === '/api/reset_odom') return send(res, 200, { ok: true, msg: 'Odometry reset' })
    if (p === '/api/calibrate_imu') return send(res, 200, { ok: true, msg: 'Calibration command sent' })
    if (p === '/api/reset_competition') {
      return send(res, 200, { ok: true, msg: `Sent competition command: ${body.command}` })
    }
    if (p === '/api/record_playback') {
      return send(res, 200, { ok: true, msg: `Sent: ${body.action}` })
    }
    if (p === '/api/logger/start') {
      Object.assign(logger, {
        is_logging: true,
        session_name: `session_${body.label || 'run'}_${Date.now()}`,
        started: Date.now(),
      })
      return send(res, 200, { ok: true, session_name: logger.session_name })
    }
    if (p === '/api/logger/stop') {
      const data = {
        session_name: logger.session_name,
        duration: +((Date.now() - logger.started) / 1000).toFixed(1),
        samples: Math.floor((Date.now() - logger.started) / 100),
      }
      logger.is_logging = false
      return send(res, 200, { ok: true, data })
    }
  }

  send(res, 404, { ok: false, error: `no route ${p}` })
})

/* ---------------- port 8000: provisioning portal + launch control ---------------- */

const portal = http.createServer(async (req, res) => {
  const p = new URL(req.url, 'http://localhost').pathname

  if (req.method === 'OPTIONS') return send(res, 204, '')

  if (p === '/api/robot_info') {
    return send(res, 200, {
      name: 'RISA-Bot 10 (mock)',
      serial: '10',
      model: 'Horizon RDK X5',
      firmware_version: '1.2.3',
      hostname: 'risabot10',
      mdns_hostname: 'risabot10.local',
    })
  }

  if (p === '/api/scan') {
    return send(res, 200, {
      networks: [
        { ssid: 'MockNet', signal: 88, security: 'WPA2' },
        { ssid: 'RISA-Lab-5G', signal: 74, security: 'WPA2' },
        { ssid: 'Competition-Hall', signal: 61, security: 'WPA2' },
        { ssid: 'Guest', signal: 43, security: 'Open' },
        { ssid: 'eduroam', signal: 22, security: 'WPA2-EAP' },
      ],
    })
  }

  if (p === '/api/status') {
    return send(res, 200, {
      wlan0_state: wifiState,
      connected_ssid: connectedSsid,
      wlan0_ip: '127.0.0.1',
      ap_ip: '192.168.4.1',
      internet_online: true,
      captive_portal: false,
      ros_service_active: true,
      launch_active: launchActive,
      dashboard_url: 'http://localhost:8080',
    })
  }

  if (p === '/api/launch_status') {
    return send(res, 200, { launch_active: launchActive, service_state: launchActive ? 'active' : 'inactive' })
  }

  if (p === '/api/model/info') {
    return send(res, 200, {
      ...model,
      sha256: new URL(req.url, 'http://localhost').searchParams.get('include_sha256') === 'false'
        ? undefined
        : model.sha256,
      launch_active: launchActive,
      backup: modelBackup ? { present: true, size: modelBackup.size, mtime_iso: modelBackup.mtime_iso } : { present: false },
    })
  }

  if (p === '/api/logs') {
    return send(res, 200, {
      has_error: false,
      error_count: 0,
      errors: [],
      logs: [
        '[auto_driver] Auto Driver Node Starting (Competition Mode)...',
        '[line_follower_camera] lane_error publishing at 31.2 Hz',
        '[ros2go2rtc_bridge] serving MJPEG on http://0.0.0.0:1985/mjpeg',
        `[auto_driver] state -> ${telemetry().state}`,
      ],
      status_summary: 'No errors found in RISA-Bot service logs.',
    })
  }

  if (req.method === 'POST') {
    if (p === '/api/connect') {
      const body = await readBody(req)
      wifiState = 'connecting'
      connectedSsid = body.ssid
      pendingPassword = body.password ?? ''
      // Mirror the real portal: respond immediately, finish in the background.
      setTimeout(() => {
        wifiState = pendingPassword === 'wrong' ? 'failed' : 'connected'
      }, 4000)
      return send(res, 200, { status: 'started', ssid: body.ssid })
    }
    if (p === '/api/launch_start') {
      launchActive = true
      return send(res, 200, { ok: true, launch_active: true, service_state: 'active' })
    }
    if (p === '/api/launch_stop') {
      launchActive = false
      return send(res, 200, { ok: true, launch_active: false, service_state: 'inactive' })
    }
    if (p === '/api/restart_ros') return send(res, 200, { status: 'active', output: 'restarted' })

    if (p === '/api/model/upload') {
      const q = new URL(req.url, 'http://localhost').searchParams
      const body = await readRaw(req)
      // Same rails as app.py: size floor, then the BPU .bin magic.
      if (body.length < 64 * 1024) {
        return send(res, 400, { detail: `Received only ${body.length} bytes; a compiled model is at least 64 KB.` })
      }
      if (!(body[0] === 0x08 && body[1] === 0x01 && body[2] === 0x10)) {
        return send(res, 400, { detail: 'That is not a compiled BPU model — an ONNX or zip was uploaded instead.' })
      }
      const got = crypto.createHash('sha256').update(body).digest('hex')
      const want = q.get('sha256')
      if (want && want.toLowerCase() !== got) {
        return send(res, 400, { detail: 'Checksum mismatch — the transfer was truncated. The existing model was left untouched.' })
      }
      modelBackup = { ...model }
      model = {
        present: true,
        path: model.path,
        name: model.name,
        size: body.length,
        mtime: Date.now() / 1000,
        mtime_iso: new Date().toISOString().slice(0, 19).replace('T', ' '),
        sha256: got,
      }
      const restarted = q.get('restart') === 'true'
      return send(res, 200, {
        ok: true, path: model.path, uploaded_name: q.get('filename'), size: body.length,
        sha256: got, backup_kept: true, restart_required: !restarted, restarted,
      })
    }

    if (p === '/api/model/rollback') {
      if (!modelBackup) return send(res, 404, { detail: 'No backup to roll back to.' })
      const restarted = new URL(req.url, 'http://localhost').searchParams.get('restart') === 'true'
      const swap = model
      model = modelBackup
      modelBackup = swap
      return send(res, 200, { ok: true, size: model.size, restart_required: !restarted, restarted })
    }
  }

  send(res, 404, { ok: false, error: `no route ${p}` })
})

/* ---------------- port 1984: stand-in for go2rtc ---------------- */

const VIEW_TINT = {
  raw: '#4b5563',
  line_follower: '#0ea5e9',
  obstacle: '#f59e0b',
  signage: '#a855f7',
  traffic_light: '#ef4444',
}

/**
 * The real robot serves multipart JPEG here. An animated SVG is not MJPEG, but it
 * renders in an <img> the same way, so the camera component can be built and laid
 * out for real without hardware.
 */
function cameraSvg() {
  const { state } = phase()
  const tint = VIEW_TINT[camView] ?? '#4b5563'
  const dashes = Array.from({ length: 6 }, (_, i) => {
    const y = i * 60
    return `<rect x="152" y="${y}" width="16" height="34" rx="4" fill="#e5e7eb" opacity="0.85">
      <animate attributeName="y" from="${y}" to="${y + 60}" dur="0.7s" repeatCount="indefinite"/>
    </rect>`
  }).join('')

  return `<svg xmlns="http://www.w3.org/2000/svg" width="320" height="240" viewBox="0 0 320 240">
  <defs><clipPath id="c"><rect width="320" height="240"/></clipPath></defs>
  <g clip-path="url(#c)">
    <rect width="320" height="240" fill="#111827"/>
    <polygon points="160,60 40,240 280,240" fill="#1f2937"/>
    <polygon points="160,60 96,240 120,240" fill="${tint}" opacity="0.25"/>
    <polygon points="160,60 200,240 224,240" fill="${tint}" opacity="0.25"/>
    ${dashes}
    <rect x="0" y="0" width="320" height="22" fill="#000" opacity="0.55"/>
    <text x="8" y="15" font-family="monospace" font-size="12" fill="${tint}">${camView}</text>
    <text x="312" y="15" font-family="monospace" font-size="12" fill="#9ca3af" text-anchor="end">${state}</text>
    <text x="312" y="232" font-family="monospace" font-size="10" fill="#6b7280" text-anchor="end">MOCK CAMERA</text>
  </g>
</svg>`
}

const camera = http.createServer((req, res) => {
  const p = new URL(req.url, 'http://localhost').pathname
  if (p === '/api/stream.mjpeg' || p === '/mjpeg') {
    return send(res, 200, cameraSvg(), 'image/svg+xml')
  }
  send(res, 404, { ok: false, error: `no route ${p}` })
})

/* ---------------- port 8000: /ws/log provisioning stream ---------------- */

const WS_GUID = '258EAFA5-E914-47DA-95CA-C5AB0DC85B11'

/** Encodes one unfragmented text frame. Payloads here are always < 64 KiB. */
function wsFrame(text) {
  const body = Buffer.from(text)
  const head =
    body.length < 126
      ? Buffer.from([0x81, body.length])
      : Buffer.concat([Buffer.from([0x81, 126]), (() => {
          const b = Buffer.alloc(2)
          b.writeUInt16BE(body.length)
          return b
        })()])
  return Buffer.concat([head, body])
}

const PROVISION_STEPS = [
  ['arp', 'associating with access point', 700],
  ['dhcp', 'requesting an address', 1100],
  ['internet_probe', 'checking internet reachability', 900],
  ['ros_check', 'waiting for ROS nodes', 1200],
]

portal.on('upgrade', (req, socket) => {
  if (new URL(req.url, 'http://localhost').pathname !== '/ws/log') return socket.destroy()

  const accept = crypto
    .createHash('sha1')
    .update(req.headers['sec-websocket-key'] + WS_GUID)
    .digest('base64')
  socket.write(
    'HTTP/1.1 101 Switching Protocols\r\n' +
      'Upgrade: websocket\r\nConnection: Upgrade\r\n' +
      `Sec-WebSocket-Accept: ${accept}\r\n\r\n`,
  )

  let alive = true
  socket.on('close', () => (alive = false))
  socket.on('error', () => (alive = false))

  const emit = (o) => alive && socket.write(wsFrame(JSON.stringify(o)))

  ;(async () => {
    for (const [status, message, delay] of PROVISION_STEPS) {
      await new Promise((r) => setTimeout(r, delay))
      if (!alive) return
      if (pendingPassword === 'wrong' && status === 'dhcp') {
        emit({ type: 'step', status: 'failed', message: 'authentication rejected by access point' })
        return
      }
      emit({ type: 'step', status, message })
    }
    emit({
      type: 'step',
      status: 'ready',
      message: 'robot is on the network',
      wlan0_ip: '127.0.0.1',
      dashboard_url: 'http://localhost:8080',
    })
  })()
})

dashboard.listen(8080, () => console.log('mock dashboard  -> http://localhost:8080'))
portal.listen(8000, () => console.log('mock portal     -> http://localhost:8000'))
camera.listen(1984, () => console.log('mock camera     -> http://localhost:1984/api/stream.mjpeg'))
console.log(`replaying a ${CYCLE}s competition lap through ${SEQUENCE.length} states`)
