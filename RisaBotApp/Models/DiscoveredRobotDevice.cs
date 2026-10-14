namespace RisaBotApp.Models;

/// <summary>
/// Model representing a discovered RISA-Bot device on the local network or hotspot.
/// Displayed as a choice card in the NoMachine-style discovery view.
/// </summary>
public class DiscoveredRobotDevice
{
    public RobotInfo Info { get; set; } = new();

    /// <summary>
    /// Connection base URL (e.g. "http://risabot10.local:8000" or "http://192.168.4.1:8000").
    /// Kept internally — raw URLs/IPs are hidden from the primary card UI.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Display name for card header (e.g. "RISA-Bot 10").</summary>
    public string DisplayName => !string.IsNullOrWhiteSpace(Info.Name) ? Info.Name : Info.Hostname;

    /// <summary>Display model (e.g. "Horizon RDK X5").</summary>
    public string DisplayModel => !string.IsNullOrWhiteSpace(Info.Model) ? Info.Model : "ROS 2 Mobile Robot";

    /// <summary>True if device was discovered on the 192.168.4.1 provisioning hotspot.</summary>
    public bool IsHotspot { get; set; }

    /// <summary>True if this robot was previously paired/remembered on this device.</summary>
    public bool IsPaired { get; set; }

    /// <summary>Network status label shown on card (e.g. "Online • Wi-Fi" or "Provisioning Hotspot").</summary>
    public string StatusText => IsHotspot
        ? "Provisioning Hotspot"
        : IsPaired
            ? "Remembered • Online"
            : "Online on Wi-Fi";

    /// <summary>
    /// One word, for the corner of a discovery card. <see cref="StatusText"/> is the
    /// explanatory form and does not fit there — a ~190px card sharing the line with the
    /// model name has room for about ten characters before it starts eliding.
    /// </summary>
    public string StatusShort => IsHotspot
        ? "setup"
        : IsPaired
            ? "known"
            : "online";

    /// <summary>Status color hex for badge styling.</summary>
    public string StatusBadgeColor => IsHotspot ? "#FF9800" : (IsPaired ? "#00E676" : "#29B6F6");
}
