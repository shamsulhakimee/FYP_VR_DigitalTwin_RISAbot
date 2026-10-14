import type {
  ApiResult,
  CameraView,
  LidarData,
  LoggerSession,
  LogsResponse,
  ModelInfo,
  ModelWriteResult,
  ParamResult,
  ProvisionFrame,
  RobotInfo,
  RobotStatus,
  Telemetry,
  WifiNetwork,
} from './types'

/** FastAPI puts the useful part of a 4xx in `detail`; a bare status hides it. */
async function errorText(res: Response) {
  try {
    const body = (await res.json()) as { detail?: string; error?: string }
    const detail = body.detail ?? body.error
    if (detail) return detail
  } catch {
    /* not JSON */
  }
  return `${res.status} ${res.statusText}`
}

/**
 * crypto.subtle only exists in a secure context. The Capacitor shell is served
 * from http://localhost so it has one, but a plain browser tab on
 * http://192.168.x.x does not — hence best-effort: the robot treats the checksum
 * as optional and simply skips verification when it is absent.
 */
export async function sha256Hex(data: ArrayBuffer): Promise<string | undefined> {
  if (!globalThis.crypto?.subtle) return undefined
  const digest = await crypto.subtle.digest('SHA-256', data)
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, '0')).join('')
}

/**
 * Every port the robot listens on is derived from one host, matching
 * ApiClient.cs in the WPF app.
 *
 * Timeouts are per-request via AbortSignal. There is deliberately no shared
 * client-wide timeout: the WPF app capped four slow calls at 10 s that way and
 * they failed silently on the happy path.
 */
export class RobotClient {
  readonly host: string

  constructor(host: string) {
    this.host = host.replace(/^https?:\/\//, '').replace(/\/$/, '')
  }

  private get dash() {
    return `http://${this.host}:8080`
  }
  private get portal() {
    return `http://${this.host}:8000`
  }
  /**
   * `src` names go2rtc's stream slot and is mandatory — without it the endpoint
   * has nothing to serve and the image just fails to load.
   *
   * There is only ever one slot: the view is switched server-side by
   * `/api/set_cam_view`, which repoints `ros2go2rtc_bridge` at a different ROS
   * topic. `view` is therefore not something go2rtc reads; it is a cache-buster,
   * so switching views cannot be answered from the previous view's still-open
   * multipart response.
   */
  cameraStreamUrl(view: CameraView) {
    return `http://${this.host}:1984/api/stream.mjpeg?src=astra_camera&view=${view}`
  }

  /** DemoClient overrides these two to answer without a network. */
  protected async json<T>(url: string, timeoutMs: number, init?: RequestInit): Promise<T> {
    const res = await fetch(url, { ...init, signal: AbortSignal.timeout(timeoutMs) })
    if (!res.ok) throw new Error(await errorText(res))
    return res.json() as Promise<T>
  }

  protected post<T>(url: string, body: unknown, timeoutMs: number) {
    return this.json<T>(url, timeoutMs, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body ?? {}),
    })
  }

  // ---- telemetry (:8080) ----

  getTelemetry(timeoutMs = 4000) {
    return this.json<Telemetry>(`${this.dash}/data`, timeoutMs)
  }

  getLidar(timeoutMs = 4000) {
    return this.json<LidarData>(`${this.dash}/lidar_data`, timeoutMs)
  }

  setCameraView(view: CameraView) {
    return this.json<ApiResult>(`${this.dash}/api/set_cam_view?view=${view}`, 5000)
  }

  // ---- parameters (:8080) ----

  getParam(node: string, param: string) {
    const q = `node=${encodeURIComponent(node)}&param=${encodeURIComponent(param)}`
    return this.json<ParamResult>(`${this.dash}/api/get_param?${q}`, 5000)
  }

  setParam(node: string, param: string, value: string) {
    return this.post<ApiResult>(`${this.dash}/api/set_param`, { node, param, value }, 5000)
  }

  saveDefaults() {
    return this.post<ApiResult>(`${this.dash}/api/save_defaults`, {}, 30_000)
  }

  // ---- competition + calibration (:8080) ----

  resetCompetition(command: 'RESET' | 'LAP1' | 'LAP2') {
    return this.post<ApiResult>(`${this.dash}/api/reset_competition`, { command }, 8000)
  }

  resetOdom() {
    return this.post<ApiResult>(`${this.dash}/api/reset_odom`, {}, 8000)
  }

  calibrateImu() {
    return this.post<ApiResult>(`${this.dash}/api/calibrate_imu`, {}, 15_000)
  }

  // ---- data logger (:8080) ----

  startLogger(label: string) {
    return this.post<ApiResult>(`${this.dash}/api/logger/start`, { label }, 8000)
  }

  stopLogger() {
    return this.post<ApiResult>(`${this.dash}/api/logger/stop`, {}, 15_000)
  }

  listLoggerSessions() {
    return this.json<{ ok: boolean; sessions: LoggerSession[] }>(
      `${this.dash}/api/logger/list`,
      8000,
    )
  }

  loggerDownloadUrl(session: string) {
    return `${this.dash}/api/logger/download?session=${encodeURIComponent(session)}`
  }

  // ---- portal (:8000) ----

  getRobotInfo(timeoutMs = 8000) {
    return this.json<RobotInfo>(`${this.portal}/api/robot_info`, timeoutMs)
  }

  getStatus(timeoutMs = 8000) {
    return this.json<RobotStatus>(`${this.portal}/api/status`, timeoutMs)
  }

  getLogs() {
    return this.json<LogsResponse>(`${this.portal}/api/logs`, 15_000)
  }

  launchStart() {
    return this.post<ApiResult>(`${this.portal}/api/launch_start`, {}, 90_000)
  }

  launchStop() {
    return this.post<ApiResult>(`${this.portal}/api/launch_stop`, {}, 90_000)
  }

  /** Returns systemd's own words: `status` is `systemctl is-active` output, not an `ok` flag. */
  restartRos() {
    return this.post<{ status: string; output: string }>(
      `${this.portal}/api/restart_ros`,
      {},
      90_000,
    )
  }

  // ---- BPU model (:8000) ----

  getModelInfo(includeSha = true) {
    return this.json<ModelInfo>(
      `${this.portal}/api/model/info?include_sha256=${includeSha}`,
      30_000,
    )
  }

  /**
   * The body is the raw .bin, not multipart — app.py streams it straight to disk to
   * avoid a python-multipart dependency. XHR rather than fetch because only XHR
   * reports upload progress, and 7.5 MB over the robot's Wi-Fi is not instant.
   */
  uploadModel(
    data: ArrayBuffer,
    opts: {
      filename: string
      sha256?: string
      restart: boolean
      onProgress?: (fraction: number) => void
    },
  ): Promise<ModelWriteResult> {
    const q = new URLSearchParams({ restart: String(opts.restart), filename: opts.filename })
    if (opts.sha256) q.set('sha256', opts.sha256)

    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest()
      xhr.open('POST', `${this.portal}/api/model/upload?${q}`)
      xhr.setRequestHeader('Content-Type', 'application/octet-stream')
      // A restart is a systemctl call the robot makes after the bytes land.
      xhr.timeout = opts.restart ? 180_000 : 120_000

      xhr.upload.onprogress = (e) => {
        if (e.lengthComputable) opts.onProgress?.(e.loaded / e.total)
      }
      xhr.onload = () => {
        let body: Record<string, unknown> = {}
        try {
          body = JSON.parse(xhr.responseText) as Record<string, unknown>
        } catch {
          /* non-JSON error page */
        }
        if (xhr.status >= 200 && xhr.status < 300) {
          resolve(body as unknown as ModelWriteResult)
        } else {
          reject(new Error((body.detail as string) ?? `${xhr.status} ${xhr.statusText}`))
        }
      }
      xhr.onerror = () => reject(new Error('Upload failed — lost contact with the robot.'))
      xhr.ontimeout = () => reject(new Error('Upload timed out.'))
      xhr.send(data)
    })
  }

  rollbackModel(restart: boolean) {
    return this.post<ModelWriteResult>(
      `${this.portal}/api/model/rollback?restart=${restart}`,
      {},
      180_000,
    )
  }

  // ---- wi-fi provisioning (:8000) ----

  scanWifi() {
    return this.json<{ networks: WifiNetwork[] }>(`${this.portal}/api/scan`, 30_000)
  }

  /** Kicks provisioning off; progress arrives on the /ws/log socket, not here. */
  connectWifi(ssid: string, password: string) {
    return this.post<{ status: string; ssid: string }>(
      `${this.portal}/api/connect`,
      { ssid, password },
      15_000,
    )
  }

  /**
   * Wrapped rather than handed out raw so DemoClient can substitute a replay:
   * a hosted build is HTTPS and `ws://` is blocked there exactly like `http://`.
   */
  openProvisionLog(): ProvisionSocket {
    const ws = new WebSocket(`ws://${this.host}:8000/ws/log`)
    return {
      onFrame(cb) {
        ws.onmessage = (ev) => {
          try {
            cb(JSON.parse(ev.data as string) as ProvisionFrame)
          } catch {
            /* not JSON */
          }
        }
      },
      onError(cb) {
        ws.onerror = () => cb()
      },
      close() {
        ws.close()
      },
    }
  }
}

export interface ProvisionSocket {
  onFrame(cb: (frame: ProvisionFrame) => void): void
  onError(cb: () => void): void
  close(): void
}
