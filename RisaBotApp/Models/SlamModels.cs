using System.Text.Json.Serialization;

namespace RisaBotApp.Models;

/// <summary>
/// Response of GET /api/slam/status on port 8080 (dashboard.py, <c>_slam_status</c>).
///
/// Everything the Map page needs except the map itself: the grid's geometry, so a pixel
/// can be turned back into metres; the robot's pose in the <c>map</c> frame; and enough
/// liveness signal to tell "slam_toolbox is thinking" from "the stack was launched with
/// <c>slam:=false</c>".
///
/// <see cref="MapSeq"/> is the cache key. It counts /map messages on the robot, so the app
/// can poll this cheap endpoint every second and only pay for the PNG when the number
/// moves — which, at a 1 s map_update_interval on a parked robot, it often does not.
/// </summary>
public class SlamStatus
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    /// <summary>
    /// Whether a map has arrived recently. False covers both "slam_toolbox is not running"
    /// and "it stopped publishing", which the app treats identically — there is nothing to
    /// draw either way.
    /// </summary>
    [JsonPropertyName("running")]
    public bool Running { get; set; }

    /// <summary>
    /// Whether scan processing is paused. Tracked on the robot from the pause service's own
    /// reported status; slam_toolbox offers no way to query it, so a slam_toolbox restart
    /// can leave this out of step until the next pause command re-syncs it.
    /// </summary>
    [JsonPropertyName("paused")]
    public bool Paused { get; set; }

    /// <summary>
    /// Whether the robot's slam_toolbox exposes a reset service. The build on this robot does
    /// not — <c>ros2 service list</c> shows save_map, pause_new_measurements, serialize and
    /// deserialize, but no reset — so Restart Mapping is retired rather than offered.
    ///
    /// Defaults to <c>true</c> so that an older dashboard, which omits the field entirely,
    /// leaves the button enabled and falls back to the error-driven path below. Defaulting to
    /// false would disable a working button against every robot not yet updated.
    /// </summary>
    [JsonPropertyName("restart_supported")]
    public bool RestartSupported { get; set; } = true;

    /// <summary>Count of /map messages received. Changes ⇒ the PNG is worth re-fetching.</summary>
    [JsonPropertyName("map_seq")]
    public int MapSeq { get; set; }

    /// <summary>Seconds since the last /map. -1 before the first one ever arrives.</summary>
    [JsonPropertyName("map_age_sec")]
    public double MapAgeSec { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    /// <summary>Metres per cell — 0.05 from mapper_params_online_async.yaml.</summary>
    [JsonPropertyName("resolution")]
    public double Resolution { get; set; }

    /// <summary>World coordinate of the grid's bottom-left corner, metres.</summary>
    [JsonPropertyName("origin_x")]
    public double OriginX { get; set; }

    [JsonPropertyName("origin_y")]
    public double OriginY { get; set; }

    /// <summary>Robot pose in the <c>map</c> frame, from TF — not from odometry, so it
    /// includes the map→odom correction that jumps at loop closure.</summary>
    [JsonPropertyName("robot_x")]
    public double RobotX { get; set; }

    [JsonPropertyName("robot_y")]
    public double RobotY { get; set; }

    /// <summary>Heading in radians, CCW from +x.</summary>
    [JsonPropertyName("robot_yaw")]
    public double RobotYaw { get; set; }

    /// <summary>False when the TF lookup failed — the marker should be hidden, not drawn
    /// at the origin, which would claim the robot is somewhere it is not.</summary>
    [JsonPropertyName("robot_valid")]
    public bool RobotValid { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>Whether the grid has real dimensions to render.</summary>
    [JsonIgnore]
    public bool HasMap => Width > 0 && Height > 0;

    /// <summary>Arena extent implied by the grid, metres. For the size readout.</summary>
    [JsonIgnore]
    public double WidthMetres => Width * Resolution;

    [JsonIgnore]
    public double HeightMetres => Height * Resolution;
}

/// <summary>
/// The rendered occupancy grid plus the sequence number it was rendered at.
///
/// The bytes are a greyscale PNG built by the dashboard's stdlib encoder, not JSON: the
/// same 300×300 grid is ~250 KB as an int array and 5–20 KB as a PNG, and WPF can decode
/// it through the same <c>BitmapImage</c> path the MJPEG reader already uses.
/// </summary>
public sealed class SlamMapImage
{
    public byte[] Png { get; init; } = Array.Empty<byte>();

    /// <summary>Value of the X-Map-Seq response header. Compared against
    /// <see cref="SlamStatus.MapSeq"/> to decide whether a re-decode is needed.</summary>
    public int Seq { get; init; }
}

/// <summary>One saved map on the robot, from GET /api/slam/maps.</summary>
public class SavedMap
{
    /// <summary>File stem — the pair on disk is <c>{name}.pgm</c> + <c>{name}.yaml</c>.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Unix seconds.</summary>
    [JsonPropertyName("mtime")]
    public long Mtime { get; set; }

    /// <summary>Size of the .pgm alone, bytes.</summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>Whether the companion .yaml is present. A .pgm without it is not loadable
    /// by map_server, so it is worth showing rather than hiding.</summary>
    [JsonPropertyName("has_yaml")]
    public bool HasYaml { get; set; }

    [JsonIgnore]
    public string SavedAtText =>
        DateTimeOffset.FromUnixTimeSeconds(Mtime).LocalDateTime.ToString("dd MMM HH:mm");

    [JsonIgnore]
    public string SizeText => Size >= 1024 * 1024
        ? $"{Size / (1024.0 * 1024.0):0.0} MB"
        : $"{Size / 1024.0:0} KB";
}

/// <summary>Response of GET /api/slam/maps.</summary>
public class SlamMapsResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("maps")]
    public List<SavedMap> Maps { get; set; } = new();

    /// <summary>Where they live on the robot (<c>~/risabot_maps</c>), shown so the operator
    /// knows where to scp from.</summary>
    [JsonPropertyName("dir")]
    public string Dir { get; set; } = string.Empty;
}

/// <summary>Response of POST /api/slam/save.</summary>
public class SlamSaveResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    /// <summary>The name actually used. The robot sanitises whatever was sent and falls
    /// back to a timestamp, so this need not match what was asked for.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
