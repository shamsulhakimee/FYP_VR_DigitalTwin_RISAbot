/**
 * Mirrors the JSON the robot already serves. Keys match dashboard.py's `get_json`
 * and app.py's response models exactly — do not rename them here.
 */

export const CHALLENGE_STATES = [
  'MANUAL',
  'FINISHED',
  'TRAFFIC_LIGHT',
  'BOOM_GATE',
  'OBSTRUCTION',
  'TUNNEL',
  'PARALLEL_PARK',
  'PERPENDICULAR_PARK',
  'LANE_FOLLOW',
  'REVERSE_ADJUST',
  'EMERGENCY_STOP',
  'ROUNDABOUT',
  'LANE_RECOVERY',
  'HILL',
  'PARKING_IDLE',
  'PARKING_PLAYBACK',
  'HILL_DESCENT',
] as const

export type ChallengeState = (typeof CHALLENGE_STATES)[number]

export type TrafficLight = 'red' | 'yellow' | 'green' | 'unknown'

export interface LoggerStatus {
  is_logging: boolean
  session_name: string
  duration_sec: number
  sample_count: number
  log_dir: string
}

/** GET :8080/data */
export interface Telemetry {
  state: ChallengeState | string
  lap: number
  auto_mode: boolean
  state_time: number
  state_dist: string
  traffic_light: TrafficLight | string
  stop_reason: string

  lidar_obstacle: boolean | null
  camera_obstacle: boolean | null
  fused_obstacle: boolean | null
  boom_gate: boolean | null
  tunnel_detected: boolean | null
  obstruction_active: boolean | null
  parking_complete: boolean | null
  parking_sign_detected: boolean | null

  lane_error: number
  cmd_lin_x: number
  cmd_ang_z: number
  distance: number
  speed: number
  odom_x: number
  odom_y: number
  odom_yaw: number

  imu_roll: number
  imu_pitch: number
  imu_yaw: number

  buttons: number[]
  axes: number[]
  speed_pct: number
  ctrl_state_index: number
  ctrl_state_name: string

  health_ok: boolean | null
  health_summary: string
  health_stale: string[]
  cmd_safety_estop: boolean
  cmd_safety_timeout_count: number
  loop_stats: Record<string, number>

  rp_state: string
  rp_buffer_size: number
  rp_playback_index: number
  rp_recording_name: string
  rp_active_parking: string
  rp_saved_recordings: string[]

  freshness_sec: Record<string, number | null>
  stale_streams: string[]
  logger_status: LoggerStatus
}

/** GET :8080/lidar_data */
export interface LidarData {
  points: { x: number; y: number }[]
  tunnel: boolean
  left_dist?: number
  right_dist?: number
  dist_error?: number
  angular_z?: number
  centerline?: { x: number; y: number }[]
}

export const CAMERA_VIEWS = [
  'raw',
  'line_follower',
  'obstacle',
  'signage',
  'traffic_light',
] as const

export type CameraView = (typeof CAMERA_VIEWS)[number]

/** GET :8000/api/robot_info */
export interface RobotInfo {
  name: string
  serial: string
  model: string
  firmware_version: string
  hostname: string
  mdns_hostname: string
}

/** GET :8000/api/status */
export interface RobotStatus {
  wlan0_state: string
  connected_ssid: string
  wlan0_ip: string
  ap_ip: string
  internet_online: boolean
  captive_portal: boolean
  ros_service_active: boolean
  launch_active: boolean
  dashboard_url: string
}

export interface ParamResult {
  ok: boolean
  value?: string
  default?: string
  error?: string
}

export interface ApiResult {
  ok: boolean
  msg?: string
  error?: string
}

/**
 * GET :8000/api/model/info. `launch_active` is what makes the "uploaded but not
 * loaded" warning possible: dnn.load() runs once in SignageDetector.__init__, so a
 * model newer than the running service is on disk but not in use.
 */
export interface ModelInfo {
  present: boolean
  path: string
  name?: string
  size?: number
  mtime?: number
  mtime_iso?: string
  sha256?: string
  sha256_error?: string
  launch_active: boolean
  backup: { present: boolean; size?: number; mtime_iso?: string }
}

/** POST :8000/api/model/upload and /api/model/rollback share this shape. */
export interface ModelWriteResult {
  ok: boolean
  size: number
  sha256?: string
  uploaded_name?: string
  backup_kept?: boolean
  restart_required: boolean
  restarted: boolean
  service_state?: string
  warning?: string
}

/** GET :8000/api/scan */
export interface WifiNetwork {
  ssid: string
  signal: number
  security: string
}

/** GET :8000/api/logs */
export interface LogsResponse {
  logs: string[]
  errors: string[]
  has_error: boolean
  error_count: number
  status_summary: string
}

/** GET :8080/api/logger/list */
export interface LoggerSession {
  name: string
  time: string
  duration: number
  samples: number
}

/**
 * One frame from the :8000/ws/log provisioning stream. `status` walks
 * arp -> dhcp -> internet_probe -> ros_check -> ready, or lands on failed.
 */
export interface ProvisionFrame {
  type: 'step' | 'pong' | 'error'
  status?: string
  message?: string
  wlan0_ip?: string
  dashboard_url?: string
}

export const PROVISION_STEPS = [
  ['arp', 'Associating'],
  ['dhcp', 'Getting an address'],
  ['internet_probe', 'Checking internet'],
  ['ros_check', 'Waiting for ROS'],
  ['ready', 'Ready'],
] as const

/** A robot found by probing :8000/api/robot_info. */
export interface DiscoveredRobot {
  host: string
  info: RobotInfo
  isHotspot: boolean
  isPaired: boolean
}

/** Persisted in localStorage — the WPF app's "paired_robot" record. */
export interface PairedRobot {
  host: string
  serial: string
  name: string
  model: string
  firmware: string
  mdns: string
  pairedAt: string
}
