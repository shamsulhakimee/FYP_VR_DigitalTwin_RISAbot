/**
 * The app driven entirely from the browser, for the hosted build.
 *
 * A page served over HTTPS cannot call the robot at all: every endpoint is
 * `http://<host>:8080` or `ws://<host>:8000`, and mixed content is blocked with no
 * user override. Vercel cannot proxy it either — the robot is on a private LAN.
 * So the hosted build answers itself.
 *
 * Subclassing RobotClient rather than reimplementing it means the URLs, timeouts
 * and response shapes stay defined in exactly one place; only the two transport
 * methods are swapped.
 */
import { RobotClient, type ProvisionSocket } from './client'
import {
  DEMO_NETWORKS,
  DEMO_PROVISION_STEPS,
  DEMO_ROBOT_INFO,
  demoCameraDataUri,
  demoLidar,
  demoLogs,
  demoModel,
  demoParam,
  demoParamCount,
  demoState,
  demoTelemetry,
  setDemoParam,
} from './demoData'
import type { CameraView, ModelWriteResult } from './types'

/** Enough that spinners and disabled states are visible, not so much it feels broken. */
const LATENCY_MS = 120

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms))

export class DemoClient extends RobotClient {
  constructor() {
    super('demo')
  }

  override cameraStreamUrl(view: CameraView) {
    return demoCameraDataUri(view)
  }

  protected override async json<T>(url: string, _timeoutMs: number): Promise<T> {
    await sleep(LATENCY_MS)
    return this.route(url) as T
  }

  protected override async post<T>(url: string, body: unknown, _timeoutMs: number): Promise<T> {
    await sleep(LATENCY_MS)
    return this.route(url, (body ?? {}) as Record<string, unknown>) as T
  }

  private route(rawUrl: string, body?: Record<string, unknown>): unknown {
    const url = new URL(rawUrl)
    const p = url.pathname
    const q = url.searchParams

    switch (p) {
      case '/data':
        return demoTelemetry()
      case '/lidar_data':
        return demoLidar()

      case '/api/set_cam_view':
        demoState.camView = q.get('view') ?? 'raw'
        return { ok: true }

      case '/api/get_param': {
        const value = demoParam(`${q.get('node')}.${q.get('param')}`)
        return { ok: true, value, default: value }
      }
      case '/api/set_param':
        setDemoParam(`${body?.node}.${body?.param}`, String(body?.value))
        return { ok: true, msg: 'OK' }
      case '/api/save_defaults':
        return { ok: true, msg: `Saved ${demoParamCount()} params to params.yaml` }

      case '/api/reset_odom':
        return { ok: true, msg: 'Odometry reset' }
      case '/api/calibrate_imu':
        return { ok: true, msg: 'Calibration command sent' }
      case '/api/reset_competition':
        return { ok: true, msg: `Sent competition command: ${body?.command}` }

      case '/api/logger/start':
        demoState.logger = {
          is_logging: true,
          session_name: `session_${body?.label || 'run'}_${Date.now()}`,
          started: Date.now(),
        }
        return { ok: true, session_name: demoState.logger.session_name }
      case '/api/logger/stop': {
        const data = {
          session_name: demoState.logger.session_name,
          duration: +((Date.now() - demoState.logger.started) / 1000).toFixed(1),
          samples: Math.floor((Date.now() - demoState.logger.started) / 100),
        }
        demoState.logger.is_logging = false
        return { ok: true, data }
      }
      case '/api/logger/status':
        return { ok: true, data: demoTelemetry().logger_status }
      case '/api/logger/list':
        return {
          ok: true,
          sessions: [
            { name: 'session_2026-08-06_141203', time: '2026-08-06 14:12:03', duration: 62.4, samples: 624 },
            { name: 'session_2026-08-06_152230', time: '2026-08-06 15:22:30', duration: 31.8, samples: 318 },
          ],
        }

      case '/api/robot_info':
        return DEMO_ROBOT_INFO
      case '/api/scan':
        return { networks: DEMO_NETWORKS }
      case '/api/status':
        return {
          wlan0_state: 'connected',
          connected_ssid: 'MockNet',
          wlan0_ip: '10.0.0.42',
          ap_ip: '192.168.4.1',
          internet_online: true,
          captive_portal: false,
          ros_service_active: true,
          launch_active: demoState.launchActive,
          dashboard_url: 'http://10.0.0.42:8080',
        }
      case '/api/logs':
        return demoLogs()

      case '/api/connect':
        return { status: 'started', ssid: body?.ssid }
      case '/api/launch_start':
        demoState.launchActive = true
        return { ok: true, launch_active: true, service_state: 'active' }
      case '/api/launch_stop':
        demoState.launchActive = false
        return { ok: true, launch_active: false, service_state: 'inactive' }
      case '/api/restart_ros':
        return { status: 'active', output: 'restarted' }

      case '/api/model/info':
        return demoModel()
      case '/api/model/rollback':
        throw new Error('No backup to roll back to.')
    }

    throw new Error(`Demo mode does not implement ${p}`)
  }

  /** Writing a model needs real hardware; refusing is more honest than faking success. */
  override uploadModel(): Promise<ModelWriteResult> {
    return Promise.reject(
      new Error('Model upload is disabled in the demo — it needs a real robot to write to.'),
    )
  }

  override openProvisionLog(): ProvisionSocket {
    const timers: ReturnType<typeof setTimeout>[] = []
    let closed = false

    return {
      onFrame(cb) {
        let at = 0
        for (const [status, message, delay] of DEMO_PROVISION_STEPS) {
          at += delay
          timers.push(
            setTimeout(() => !closed && cb({ type: 'step', status, message }), at),
          )
        }
        timers.push(
          setTimeout(
            () =>
              !closed &&
              cb({
                type: 'step',
                status: 'ready',
                message: 'robot is on the network',
                wlan0_ip: '10.0.0.42',
              }),
            at + 700,
          ),
        )
      },
      onError() {
        /* the demo never loses contact */
      },
      close() {
        closed = true
        timers.forEach(clearTimeout)
      },
    }
  }
}
