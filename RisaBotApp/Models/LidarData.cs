using System.Text.Json.Serialization;

namespace RisaBotApp.Models;

/// <summary>
/// A single point in the robot's own frame, metres. +x is forward, +y is left.
/// Serialised as an object rather than a pair, matching dashboard.py:718.
/// </summary>
public class LidarPoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

/// <summary>
/// Response of GET /lidar_data on port 8080 (dashboard.py:1019).
///
/// Polled on its own timer at 250 ms — faster than the 500 ms telemetry poll, because a
/// scan plot that lags by half a second reads as broken while a speed readout that does
/// not. It is a separate request so that neither rate has to be a compromise.
///
/// The five tunnel fields exist only while tunnel_wall_follower is publishing debug; the
/// handler omits them entirely otherwise, which is why they are nullable and the
/// centerline defaults to empty rather than null. <see cref="HasTunnelDebug"/> is the test
/// worth using, not a null check on any one of them.
/// </summary>
public class LidarData
{
    [JsonPropertyName("points")]
    public List<LidarPoint> Points { get; set; } = new();

    /// <summary>Mirror of the state machine's tunnel flag, included so the plot can tint
    /// its detection wedges without a second request.</summary>
    [JsonPropertyName("tunnel")]
    public bool Tunnel { get; set; }

    /// <summary>Mean distance to the left wall, metres.</summary>
    [JsonPropertyName("left_dist")]
    public double? LeftDist { get; set; }

    /// <summary>Mean distance to the right wall, metres.</summary>
    [JsonPropertyName("right_dist")]
    public double? RightDist { get; set; }

    /// <summary>Lateral error off the centreline, metres. Signed.</summary>
    [JsonPropertyName("dist_error")]
    public double? DistError { get; set; }

    /// <summary>The angular velocity the wall follower is asking for, rad/s. Signed:
    /// positive turns left.</summary>
    [JsonPropertyName("angular_z")]
    public double? AngularZ { get; set; }

    /// <summary>At most ten points (tunnel_wall_follower.py:326), so this is a sketch of
    /// the line rather than a dense path.</summary>
    [JsonPropertyName("centerline")]
    public List<LidarPoint> Centerline { get; set; } = new();

    /// <summary>
    /// Whether the wall follower is publishing debug at all. The handler adds all five
    /// fields together or none of them, so one is enough to test.
    /// </summary>
    [JsonIgnore]
    public bool HasTunnelDebug => AngularZ.HasValue;
}
