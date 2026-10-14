namespace RisaBotApp.Models;

/// <summary>One tunable ROS parameter: the name the node knows it by, and what it does.</summary>
/// <param name="Name">Parameter name as declared by the node.</param>
/// <param name="Tip">Operator-facing description, shown as a tooltip on the row.</param>
public sealed record ParamSpec(string Name, string Tip);

/// <summary>
/// A collapsible section of the parameters drawer: one node, one heading, a fixed list of
/// parameters.
///
/// A node may appear in more than one group — <c>auto_driver</c> has three, because sixteen
/// driving parameters plus fourteen hill-slope ones in a single list is unreadable. Groups are
/// therefore identified by the object, never by <see cref="Node"/>.
/// </summary>
public sealed record ParamGroup(string Node, string Label, IReadOnlyList<ParamSpec> Params);

/// <summary>
/// The curated list of live-tunable parameters, ported from <c>PARAM_GROUPS</c> and
/// <c>PARAM_TIPS</c> in <c>dashboard_templates.py</c> (lines 1929–2171).
///
/// Curated, not discovered: the robot has no endpoint that lists a node's parameters, and
/// <c>ros2 param list</c> would return hundreds including QoS and use_sim_time noise. The
/// upside of hardcoding is that each entry carries a description; the cost is that a new
/// parameter in params.yaml is invisible here until it is added below.
///
/// Two things were fixed rather than ported, both bugs in the web original:
///
/// 1. <b>Groups are objects, not node lookups.</b> The web version's
///    <c>toggleNodeParams(el, nodeName)</c> resolves its parameter list with
///    <c>PARAM_GROUPS.find(g =&gt; g.node === nodeName)</c>. Since <c>auto_driver</c> owns three
///    groups, expanding "Hill Climb" or "Hill Descent" there fetches the *Auto Driver* list
///    instead and the hill parameters never appear. Here each group loads its own
///    <see cref="ParamGroup.Params"/>.
///
/// 2. <b><c>obstacle_avoidance_camera</c> appears once.</b> The web version lists it twice
///    with six parameters in common, and because its DOM ids are <c>pv_{node}_{param}</c> the
///    duplicate ids mean the second copy's inputs are dead — writes land in the first group's
///    boxes. The two lists are merged below into the union of nine.
///
/// Tips are folded into each spec rather than kept in a parallel name→text dictionary. Same
/// text, but a parameter that means different things on different nodes can now say so:
/// <c>forward_speed</c> is a straight-line cap for auto_driver and a crawl speed for the
/// tunnel follower.
/// </summary>
public static class ParameterCatalog
{
    private static ParamSpec P(string name, string tip) => new(name, tip);

    /// <summary>Every group, in the order the drawer shows them.</summary>
    public static IReadOnlyList<ParamGroup> Groups { get; } =
    [
        new ParamGroup("line_follower_camera", "Line Follower",
        [
            P("n_scanlines",              "Number of horizontal scanlines to sample"),
            P("min_valid_scanlines",      "Min scanlines for confident lock"),
            P("min_line_width_px",        "Min white region width in pixels (noise filter)"),
            P("crop_ratio_base",          "Bottom crop ratio — how much of the frame is road"),
            P("search_radius_px",         "Blob-to-expected-position match radius in pixels"),
            P("white_threshold",          "Fixed gray threshold for binary (used when use_otsu=false)"),
            P("use_otsu",                 "Use Otsu auto-threshold instead of fixed white_threshold"),
            P("invert_binary",            "Invert binary image — detect dark lane (true) or white borders (false)"),
            P("morph_open_size",          "Morphological OPEN kernel size — removes small noise blobs (0=disable, 3=default)"),
            P("morph_close_size",         "Morphological CLOSE kernel size — fills small gaps in lines (0=disable, 5=default)"),
            P("clahe_enabled",            "Enable CLAHE adaptive lighting normalization"),
            P("clahe_clip_limit",         "CLAHE contrast clip limit"),
            P("ipm_enabled",              "Enable Birds Eye View perspective warp (MDPI)"),
            P("ipm_top_width_ratio",      "Narrow end of trapezoid (0.2-0.5, lower=stronger warp)"),
            P("ipm_bottom_width_ratio",   "Wide end of trapezoid (usually 1.0)"),
            P("kalman_enabled",           "Use Kalman filter instead of EMA for lane smoothing"),
            P("kalman_process_noise",     "Q — how much the filter trusts its model (lower=smoother)"),
            P("kalman_measurement_noise", "R — how much the filter trusts measurements (lower=more reactive)"),
            P("smoothing_alpha",          "EMA smoothing (0=smooth, 1=raw) — used when Kalman disabled"),
            P("dead_zone",                "Tolerance threshold — ignore error below this"),
            P("hold_error_frames",        "Frames to hold last known error when lines lost"),
            P("error_decay_rate",         "Per-frame decay for held error (0.92 = halves in ~9 frames)"),
            P("resize_width",             "Resize width for CV speed"),
            P("print_debug",              "Enable console debug printing"),
            P("debug_print_rate",         "Seconds between console debug prints"),
            P("show_debug",               "Publish annotated debug frame"),
        ]),

        new ParamGroup("auto_driver", "Auto Driver",
        [
            P("forward_speed",                "Max forward speed (m/s) — on a straight"),
            P("stale_timeout",                "Seconds before module data is stale"),
            P("dist_lap_complete",            "Distance after green to mark lap"),
            P("enable_subsumption_obstacle",  "Enable fused obstacle reverse adjust"),
            P("max_odom_speed",               "Ignore odom speed spikes above this"),
            P("min_state_dwell_sec",          "Minimum hold time for non-emergency behavior switches"),
            P("publish_loop_stats",           "Enable loop timing diagnostics stream"),
            P("pid_kp",                       "[PID] Proportional gain — how hard to steer. Too high → oscillation/weaving"),
            P("pid_ki",                       "[PID] Integral gain — corrects long-term drift. Keep very small (0.0–0.05)"),
            P("pid_kd",                       "[PID] Derivative gain — dampens oscillation. Too high → jittery steering"),
            P("pid_integral_max",             "[PID] Anti-windup clamp for I term. Prevents integral runaway in long turns"),
            P("speed_error_scale",            "Adaptive speed: higher = robot slows more in turns (try 1.0–2.5)"),
            P("min_turn_speed",               "Adaptive speed: minimum speed multiplier in a sharp turn (0.4 = 40% of max)"),
            P("lane_steer_slew",              "Max steering change per second — lower = smoother but slower response (Cytron accel limit)"),
            P("t_post_obstacle_sec",          "Lane-follow delay after obstacle clears before entering roundabout (sec)"),
            P("t_roundabout_sec",             "Time to traverse the roundabout arc before exiting (sec)"),
        ]),

        new ParamGroup("auto_driver", "⛰ Hill Climb",
        [
            P("hill_pitch_threshold",  "Nose-up pitch (deg) to enter HILL climb mode"),
            P("hill_pitch_hysteresis", "Hysteresis band (deg) — exits HILL when pitch drops below threshold minus this"),
            P("hill_base_speed",       "Forward speed (m/s) at the moment pitch crosses the climb threshold"),
            P("hill_speed_per_degree", "Extra speed added per degree above climb threshold (e.g. 0.006 * 10 deg = +0.06 m/s)"),
            P("hill_max_speed",        "Maximum speed cap in HILL climb mode (m/s)"),
            P("hill_steer_scale",      "Fraction of lane-follow steering to keep on the hill (0=go straight, 1=full steer)"),
            P("hill_sign_prime_sec",   "Window (sec) after hill sign during which the pitch threshold is lowered"),
            P("hill_sign_prime_threshold_reduction",
                                       "How much to reduce the pitch threshold during the prime window (deg)"),
        ]),

        new ParamGroup("auto_driver", "⛷ Hill Descent",
        [
            P("descent_pitch_threshold",  "Nose-down pitch magnitude (deg) to enter HILL_DESCENT mode (compared to abs(pitch))"),
            P("descent_pitch_hysteresis", "Hysteresis band (deg) for exiting HILL_DESCENT (exits when |pitch| < threshold - hyst)"),
            P("descent_base_speed",       "Forward speed (m/s) at the descent entry threshold — keep low since gravity assists"),
            P("descent_speed_per_degree", "Speed REDUCTION per extra degree below descent threshold (subtracted from base)"),
            P("descent_min_speed",        "Floor speed (m/s) during descent — prevents robot from stopping completely on the slope"),
            P("descent_steer_scale",      "Fraction of lane-follow steering to retain on descent (0=straight, 0.5=half steer)"),
        ]),

        new ParamGroup("cmd_safety_controller", "Cmd Safety",
        [
            P("publish_hz",         "Safety controller publish loop frequency"),
            P("cmd_timeout",        "Max age for raw auto commands before forced zero"),
            P("max_linear_speed",   "Clamp for linear auto command speed"),
            P("max_angular_speed",  "Clamp for angular auto command speed"),
            P("max_linear_accel",   "Linear acceleration slew limit"),
            P("max_angular_accel",  "Angular acceleration slew limit"),
            P("deadband_linear",    "Zero small linear command noise"),
            P("deadband_angular",   "Zero small angular command noise"),
            P("publish_loop_stats", "Enable loop timing diagnostics stream"),
        ]),

        // Union of the web version's two "Camera Obstacle" groups — see the class remarks.
        new ParamGroup("obstacle_avoidance_camera", "Camera Obstacle",
        [
            P("edge_threshold",  "Edge pixel ratio to trigger (0.0-1.0)"),
            P("canny_low",       "Canny lower threshold"),
            P("canny_high",      "Canny upper threshold"),
            P("blur_kernel",     "Gaussian blur kernel (odd number)"),
            P("hysteresis_on",   "Frames to confirm obstacle"),
            P("hysteresis_off",  "Frames to confirm clear"),
            P("resize_width",    "Resize width for CV speed"),
            P("heartbeat_sec",   "Publish heartbeat period"),
            P("show_debug",      "Publish annotated debug frame"),
        ]),

        new ParamGroup("boom_gate_detector", "Boom Gate",
        [
            P("enable_camera",         "Also confirm the gate from the camera, not LiDAR alone"),
            P("cam_red_min_width",     "Min width (px) of the red gate arm in the camera frame"),
            P("cam_red_sat_min",       "Min saturation for the gate's red to count"),
            P("cam_red_val_min",       "Min brightness for the gate's red to count"),
            P("min_detect_dist",       "Closest gate detection (m)"),
            P("max_detect_dist",       "Farthest gate detection (m)"),
            P("angle_window",          "Forward arc width (rad)"),
            P("min_gate_points",       "Min LiDAR points for gate"),
            P("distance_variance_max", "Max spread of gate points"),
            P("lidar_angle_offset",    "LiDAR mount rotation (rad). 3.1416 = LiDAR 0° points backward"),
            P("hysteresis",            "Consecutive frames before state change"),
            P("heartbeat_sec",         "Publish heartbeat period"),
        ]),

        new ParamGroup("tunnel_wall_follower", "Tunnel",
        [
            P("target_center_dist",       "Offset from center between walls (0=centered)"),
            P("forward_speed",            "Crawl speed (m/s) while following the tunnel walls"),
            P("kp",                       "Proportional gain on lateral offset from the tunnel centre"),
            P("kd",                       "Derivative gain on lateral offset — dampens the high P gain"),
            P("kp_heading",               "Proportional gain for heading alignment (wall angle correction)"),
            P("kd_heading",               "Derivative gain for heading alignment (dampens heading oscillation)"),
            P("max_angular",              "Clamp on the turn rate this node may request (rad/s)"),
            P("left_angle_min",           "Left wall scan arc start (rad), e.g. 0.26 = 15°"),
            P("left_angle_max",           "Left wall scan arc end (rad), e.g. 2.09 = 120°"),
            P("right_angle_min",          "Right wall scan arc start (rad), negative mirror of the left arc"),
            P("right_angle_max",          "Right wall scan arc end (rad)"),
            P("min_wall_points",          "Min LiDAR points on each side to consider a wall present"),
            P("max_wall_dist",            "Ignore LiDAR points beyond this distance (m)"),
            P("lidar_angle_offset",       "LiDAR mount rotation (rad). 3.1416 = LiDAR 0° points backward"),
            P("ransac_threshold",         "RANSAC inlier distance threshold in meters (lower=stricter fit)"),
            P("ransac_iterations",        "RANSAC max iterations (50 is fine for <100 LiDAR pts)"),
            P("tunnel_hysteresis_frames", "Consecutive frames required before toggling tunnel on/off (prevents flicker)"),
            P("heartbeat_sec",            "Publish heartbeat period"),
        ]),

        new ParamGroup("obstruction_avoidance", "Obstruction",
        [
            P("detect_dist",         "Start dodging at this distance (m)"),
            P("clear_dist",          "Consider clear beyond this (m)"),
            P("front_angle",         "Front detection arc (rad)"),
            P("side_angle_min",      "Side arc start (rad)"),
            P("side_angle_max",      "Side arc end (rad)"),
            P("steer_speed",         "Speed while steering around (m/s)"),
            P("steer_angular",       "Turn rate while dodging (rad/s)"),
            P("pass_speed",          "Speed while passing alongside (m/s)"),
            P("pass_duration",       "Seconds driving alongside"),
            P("steer_back_duration", "Seconds steering back to lane"),
            P("steer_away_duration", "Seconds steering away"),
            P("lidar_angle_offset",  "LiDAR mount rotation (rad). 3.1416 = LiDAR 0° points backward"),
        ]),

        new ParamGroup("parking_controller", "Parking",
        [
            P("parallel_forward_dist",   "Drive past slot distance (m)"),
            P("parallel_reverse_dist",   "Reverse into slot distance (m)"),
            P("parallel_steer_angle",    "Steering rate during reverse (rad/s)"),
            P("perp_turn_angle",         "Turn angle into slot (rad)"),
            P("perp_forward_dist",       "Drive into slot distance (m)"),
            P("park_wait_time",          "Seconds to wait in slot"),
            P("drive_speed",             "Parking drive speed (m/s)"),
            P("reverse_speed",           "Parking reverse speed (m/s)"),
            P("signboard_min_area",      "Min contour area for parking sign"),
            P("signboard_resize_width",  "Resize width for sign detection"),
        ]),

        new ParamGroup("obstacle_avoidance_node", "LiDAR Obstacle",
        [
            P("min_obstacle_distance", "Report an obstacle when a LiDAR return falls inside this range (m)"),
            P("heartbeat_sec",         "Publish heartbeat period"),
        ]),

        new ParamGroup("servo_controller", "Servo/Odom",
        [
            P("servo_center",             "Servo neutral angle (90=default). Adjust if robot drifts left/right when steering is centered"),
            P("servo_range_left",         "Left steering range from center (50=default). Increase for sharper left turns"),
            P("servo_range_right",        "Right steering range from center (50=default). Increase for sharper right turns"),
            P("servo_steer_id",           "Servo channel for steering (4=default)"),
            P("joy_timeout",              "Joystick stream timeout before the manual command is dropped (s)"),
            P("auto_cmd_timeout",         "Auto cmd stream timeout before forcing manual stop"),
            P("unlock_requires_neutral",  "Require neutral sticks after unlock before driving"),
            P("unlock_neutral_threshold", "Neutral stick threshold for unlock gate"),
            P("ticks_per_meter",          "Encoder ticks per meter"),
            P("drive_motor_index",        "Which motor encoder channel to use (0=FL 1=FR 2=RL 3=RR)"),
            P("odom_distance_scale",      "Extra distance calibration multiplier"),
            P("odom_yaw_scale",           "Extra yaw calibration multiplier"),
            P("encoder_jump_threshold",   "Reject tick jumps above this"),
            P("max_linear_velocity",      "Clamp linear odom speed"),
            P("max_angular_velocity",     "Clamp angular odom speed"),
            P("wheel_base",               "Wheelbase (m) used by the odometry model — measure, don't guess"),
            P("steering_max_deg",         "Mechanical steering limit (deg) at full servo range"),
            P("odom_vel_alpha",           "EMA smoothing on reported odom velocity (0=smooth, 1=raw)"),
            P("odom_velocity_deadband",   "Zero odom near standstill"),
            P("odom_reverse_polarity",    "Invert encoder odom sign if needed"),
            P("publish_loop_stats",       "Enable loop timing diagnostics stream"),
        ]),

        new ParamGroup("dashboard", "Dashboard",
        [
            P("use_hw_odom",         "Use hardware encoder odometry"),
            P("freshness_stale_sec", "Dashboard stale threshold in seconds"),
            P("sim_odom_scale",      "Scale for simulated odometry distance"),
            P("hw_odom_scale",       "Scale for hardware odometry distance"),
            P("hw_odom_yaw_scale",   "Scale for hardware odometry yaw"),
        ]),

        new ParamGroup("health_monitor", "Health Monitor",
        [
            P("publish_period",      "Health publish period (s)"),
            P("timeout_perception",  "Perception timeout (s)"),
            P("timeout_state",       "State timeout (s)"),
            P("timeout_control",     "Control timeout (s)"),
            P("timeout_odom",        "Odometry timeout (s)"),
            P("timeout_joy",         "Joystick timeout (s)"),
        ]),

        new ParamGroup("signage_detector", "Signage Detector (BPU)",
        [
            P("model_path",             "Path to compiled BPU .bin model file"),
            P("conf_threshold",         "Global YOLO confidence fallback (0.0–1.0)"),
            P("iou_threshold",          "NMS IoU threshold (0.0–1.0)"),
            P("min_parking_sign_width", "Min pixel width for parking sign trigger (0 = disabled)"),
            P("heartbeat_sec",          "Publish heartbeat period"),
            P("show_debug",             "Publish annotated debug frame"),
            P("thresh_bumper",          "Confidence threshold for Bumper_signboard (class 0)"),
            P("thresh_hill",            "Confidence threshold for Hill_signboard (class 1)"),
            P("thresh_obstacle",        "Confidence threshold for Obstacle_signboard (class 2)"),
            P("thresh_parallelp",       "Confidence threshold for ParallelP_signboard (class 3)"),
            P("thresh_perpendp",        "Confidence threshold for PerpendP_signboard (class 4)"),
            P("thresh_roundabout",      "Confidence threshold for Roundabout_signboard (class 5)"),
            P("thresh_tl_green",        "Confidence threshold for Traffic_Green (class 6)"),
            P("thresh_tl_red",          "Confidence threshold for Traffic_Red (class 7)"),
            P("thresh_tl_generic",      "Confidence threshold for Trafficlight_signboard generic (class 8)"),
            P("color_bumper",           "Bounding box color for Bumper_signboard — format \"B,G,R\" e.g. 0,140,255"),
            P("color_hill",             "Bounding box color for Hill_signboard — format \"B,G,R\" e.g. 180,0,255"),
            P("color_obstacle",         "Bounding box color for Obstacle_signboard — format \"B,G,R\" e.g. 0,255,255"),
            P("color_parallelp",        "Bounding box color for ParallelP_signboard — format \"B,G,R\" e.g. 255,0,0"),
            P("color_perpendp",         "Bounding box color for PerpendP_signboard — format \"B,G,R\" e.g. 0,100,0"),
            P("color_roundabout",       "Bounding box color for Roundabout_signboard — format \"B,G,R\" e.g. 255,255,255"),
            P("color_tl_green",         "Bounding box color for Traffic_Green — format \"B,G,R\" e.g. 0,255,0"),
            P("color_tl_red",           "Bounding box color for Traffic_Red — format \"B,G,R\" e.g. 0,0,255"),
            P("color_tl_generic",       "Bounding box color for Trafficlight_signboard generic — format \"B,G,R\" e.g. 255,255,0"),
        ]),
    ];
}
