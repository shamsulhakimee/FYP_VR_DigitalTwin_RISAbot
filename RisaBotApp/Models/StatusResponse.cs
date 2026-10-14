using System.Text.Json.Serialization;

namespace RisaBotApp.Models;

/// <summary>
/// Response from GET /api/status.
/// Reflects wlan0 connection state, internet reachability, and ROS 2 service health.
/// </summary>
public class StatusResponse
{
    [JsonPropertyName("wlan0_state")]
    public string Wlan0State { get; set; } = string.Empty;

    [JsonPropertyName("connected_ssid")]
    public string? ConnectedSsid { get; set; }

    [JsonPropertyName("wlan0_ip")]
    public string? Wlan0Ip { get; set; }

    [JsonPropertyName("ap_ip")]
    public string ApIp { get; set; } = string.Empty;

    [JsonPropertyName("internet_online")]
    public bool InternetOnline { get; set; }

    /// <summary>
    /// Something answered the connectivity probe, but not the empty 204 the real
    /// endpoint sends — i.e. a sign-in page is standing in front of the network.
    /// Distinct from plain offline because the robot is headless and can never
    /// clear a portal on its own; the fix is a MAC whitelist, not a retry.
    /// </summary>
    [JsonPropertyName("captive_portal")]
    public bool CaptivePortal { get; set; }

    /// <summary>Channel wlan0's uplink is on, or null when it isn't associated.</summary>
    [JsonPropertyName("sta_channel")]
    public int? StaChannel { get; set; }

    /// <summary>Channel the hotspot is beaconing on.</summary>
    [JsonPropertyName("ap_channel")]
    public int? ApChannel { get; set; }

    /// <summary>
    /// The two channels above disagree. ap0 and wlan0 are virtual interfaces on
    /// one physical radio, and a radio tunes to one channel at a time — so when
    /// they disagree the uplink is associated on paper and carries nothing in
    /// practice. Reads from outside as "joined the network, no internet".
    /// </summary>
    [JsonPropertyName("channel_conflict")]
    public bool ChannelConflict { get; set; }

    [JsonPropertyName("ros_service_active")]
    public bool RosServiceActive { get; set; }

    [JsonPropertyName("launch_active")]
    public bool LaunchActive { get; set; }

    /// <summary>Preferred hostname-based dashboard URL (e.g. http://risabot9.local/dashboard).</summary>
    [JsonPropertyName("dashboard_url")]
    public string DashboardUrl { get; set; } = string.Empty;

    [JsonPropertyName("hostname_dashboard_url")]
    public string HostnameDashboardUrl { get; set; } = string.Empty;

    [JsonPropertyName("web_dashboard_url")]
    public string WebDashboardUrl { get; set; } = string.Empty;

    [JsonPropertyName("wlan0_dashboard_url")]
    public string Wlan0DashboardUrl { get; set; } = string.Empty;
}
