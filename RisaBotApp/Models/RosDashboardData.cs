using System.Text.Json.Serialization;

namespace RisaBotApp.Models;

/// <summary>
/// Model representing the live ROS 2 telemetry data returned by GET /data on port 8080.
///
/// Mirrors the <c>self.data</c> dict built in dashboard.py (line 331). Anything the web
/// dashboard can show is reachable from here; keep the two in step, because a key that
/// exists on the robot but not in this class simply vanishes on deserialisation without so
/// much as a warning.
///
/// The nullable bools are not laziness. The node seeds those entries as None and only
/// replaces them once the corresponding topic has published, so null means "this subsystem
/// has not reported yet" and is a different thing from false. The sensor dots render all
/// three states.
/// </summary>
public class RosDashboardData
{
    // ─── state machine ─────────────────────────────────────────────────────────

    [JsonPropertyName("state")]
    public string State { get; set; } = "UNKNOWN";

    [JsonPropertyName("lap")]
    public int Lap { get; set; } = 1;

    [JsonPropertyName("auto_mode")]
    public bool AutoMode { get; set; }

    /// <summary>
    /// Seconds since the state machine last changed state. Computed server-side at request
    /// time (dashboard.py:763) rather than pushed, so it is always current rather than as
    /// old as the last transition.
    /// </summary>
    [JsonPropertyName("state_time")]
    public int StateTime { get; set; }

    /// <summary>
    /// Total distance travelled, already formatted to two decimals by the robot. A string
    /// and not a double on purpose: the node stores the text it parsed off the state topic
    /// and never converts it back, so parsing it here would only add a way to fail.
    /// </summary>
    [JsonPropertyName("state_dist")]
    public string StateDist { get; set; } = "0.00";

    /// <summary>Why the robot last stopped. Empty when it did not.</summary>
    [JsonPropertyName("stop_reason")]
    public string StopReason { get; set; } = string.Empty;

    // ─── motion ────────────────────────────────────────────────────────────────

    [JsonPropertyName("speed")]
    public double Speed { get; set; }

    [JsonPropertyName("distance")]
    public double Distance { get; set; }

    [JsonPropertyName("odom_x")]
    public double OdomX { get; set; }

    [JsonPropertyName("odom_y")]
    public double OdomY { get; set; }

    [JsonPropertyName("odom_yaw")]
    public double OdomYaw { get; set; }

    [JsonPropertyName("lane_error")]
    public double LaneError { get; set; }

    [JsonPropertyName("cmd_lin_x")]
    public double CmdLinX { get; set; }

    [JsonPropertyName("cmd_ang_z")]
    public double CmdAngZ { get; set; }

    // ─── IMU ───────────────────────────────────────────────────────────────────

    [JsonPropertyName("imu_roll")]
    public double ImuRoll { get; set; }

    [JsonPropertyName("imu_pitch")]
    public double ImuPitch { get; set; }

    [JsonPropertyName("imu_yaw")]
    public double ImuYaw { get; set; }

    // ─── perception ────────────────────────────────────────────────────────────

    [JsonPropertyName("traffic_light")]
    public string TrafficLight { get; set; } = "unknown";

    /// <summary>Obstacle seen by the LiDAR alone.</summary>
    [JsonPropertyName("lidar_obstacle")]
    public bool? LidarObstacle { get; set; }

    /// <summary>Obstacle seen by the camera alone.</summary>
    [JsonPropertyName("camera_obstacle")]
    public bool? CameraObstacle { get; set; }

    /// <summary>The fusion of the two above — this is the one the state machine acts on.</summary>
    [JsonPropertyName("fused_obstacle")]
    public bool? FusedObstacle { get; set; }

    [JsonPropertyName("boom_gate")]
    public bool? BoomGate { get; set; }

    [JsonPropertyName("tunnel_detected")]
    public bool? TunnelDetected { get; set; }

    [JsonPropertyName("obstruction_active")]
    public bool? ObstructionActive { get; set; }

    [JsonPropertyName("parking_complete")]
    public bool? ParkingComplete { get; set; }

    [JsonPropertyName("parking_sign_detected")]
    public bool? ParkingSignDetected { get; set; }

    // ─── health and safety ─────────────────────────────────────────────────────

    [JsonPropertyName("health_ok")]
    public bool? HealthOk { get; set; }

    [JsonPropertyName("health_summary")]
    public string HealthSummary { get; set; } = string.Empty;

    /// <summary>
    /// Names of the streams that have gone quiet. The web dashboard shows the count as a
    /// dot and the names on hover; an empty list is the healthy case.
    /// </summary>
    [JsonPropertyName("health_stale")]
    public List<string> HealthStale { get; set; } = new();

    /// <summary>Whether cmd_safety_controller currently has the brakes on.</summary>
    [JsonPropertyName("cmd_safety_estop")]
    public bool CmdSafetyEstop { get; set; }

    /// <summary>
    /// How many times the safety controller has zeroed the velocity because no command
    /// arrived in time. Monotonic for the life of the node, so it is a running total and
    /// not a rate — the interesting thing is that it moves, not what it reads.
    /// </summary>
    [JsonPropertyName("cmd_safety_timeout_count")]
    public int CmdSafetyTimeoutCount { get; set; }

    /// <summary>
    /// Per-node loop timing, keyed by node name. Free-form on the robot side, so it is
    /// carried as a dictionary of raw JSON rather than given a shape it might outgrow.
    /// </summary>
    [JsonPropertyName("loop_stats")]
    public Dictionary<string, System.Text.Json.JsonElement> LoopStats { get; set; } = new();

    // ─── manual control ────────────────────────────────────────────────────────

    /// <summary>Gamepad button states, in controller index order. Empty until a joystick
    /// publishes; do not index into it without checking the length.</summary>
    [JsonPropertyName("buttons")]
    public List<int> Buttons { get; set; } = new();

    /// <summary>Gamepad axis values, in controller index order, each roughly -1..1.</summary>
    [JsonPropertyName("axes")]
    public List<double> Axes { get; set; } = new();

    /// <summary>Speed cap as a percentage, stepped by the gear buttons. Defaults to 25.</summary>
    [JsonPropertyName("speed_pct")]
    public int SpeedPct { get; set; } = 25;

    [JsonPropertyName("ctrl_state_index")]
    public int CtrlStateIndex { get; set; }

    [JsonPropertyName("ctrl_state_name")]
    public string CtrlStateName { get; set; } = "LANE_FOLLOW";

    // ─── record and playback ───────────────────────────────────────────────────

    /// <summary>One of IDLE, RECORDING, PLAYBACK.</summary>
    [JsonPropertyName("rp_state")]
    public string RpState { get; set; } = "IDLE";

    [JsonPropertyName("rp_buffer_size")]
    public int RpBufferSize { get; set; }

    [JsonPropertyName("rp_playback_index")]
    public int RpPlaybackIndex { get; set; }

    [JsonPropertyName("rp_recording_name")]
    public string RpRecordingName { get; set; } = string.Empty;

    [JsonPropertyName("rp_active_parking")]
    public string RpActiveParking { get; set; } = string.Empty;

    [JsonPropertyName("rp_saved_recordings")]
    public List<string> RpSavedRecordings { get; set; } = new();

    // ─── subsystem data logger ─────────────────────────────────────────────────

    /// <summary>
    /// Live state of the CSV data logger, folded into this payload by the robot
    /// (dashboard.py:780) rather than being a separate endpoint.
    ///
    /// That is why the Data Logger screen has no poll loop of its own: the same status is
    /// also served by <c>/api/logger/status</c>, but it is already arriving here every
    /// 500 ms for free, and the duration and sample counters are the two values on that
    /// screen that most need to look live.
    ///
    /// Null when the node has no logger attached — which in practice means an older robot
    /// build, since the current dashboard always constructs one.
    /// </summary>
    [JsonPropertyName("logger_status")]
    public LoggerStatus? LoggerStatus { get; set; }
}
