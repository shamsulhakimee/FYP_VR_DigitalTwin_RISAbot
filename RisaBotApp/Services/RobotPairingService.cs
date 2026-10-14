using System.Text.Json;
using RisaBotApp.Models;

namespace RisaBotApp.Services;

/// <summary>
/// Persists paired-robot data to the local settings store.
/// Remembers identity, target Wi-Fi SSID, mDNS hostname, and last assigned wlan0 IP for network-aware discovery.
/// </summary>
public class RobotPairingService(SettingsStore settings)
{
    private const string PrefsKey = "paired_robot";

    private readonly SettingsStore _settings = settings;

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = false
    };

    // ── Read ────────────────────────────────────────────────────────────────

    /// <summary>Returns the stored pairing, or null if the robot has never been paired on this device.</summary>
    public PairedRobot? GetPairedRobot()
    {
        var json = _settings.Get(PrefsKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json)) return null;

        try { return JsonSerializer.Deserialize<PairedRobot>(json, _json); }
        catch { return null; /* Corrupt data — treat as unpaired */ }
    }

    // ── Write ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Persist the paired robot identity after a successful Wi-Fi provisioning flow.
    /// Stores the target network SSID, mDNS hostname, and wlan0 IP fallback.
    /// </summary>
    public void SavePairedRobot(RobotInfo info, string targetNetworkSsid, string wlan0Ip, string dashboardUrl)
    {
        var robot = new PairedRobot
        {
            Serial            = info.Serial,
            Name              = info.Name,
            Model             = info.Model,
            FirmwareVersion   = info.FirmwareVersion,
            TargetNetworkSsid = targetNetworkSsid ?? string.Empty,
            MdnsHostname      = info.MdnsHostname,
            Wlan0Ip           = wlan0Ip ?? string.Empty,
            DashboardUrl      = dashboardUrl,
            PairedAt          = DateTime.UtcNow,
        };
        _settings.Set(PrefsKey, JsonSerializer.Serialize(robot, _json));
    }

    /// <summary>
    /// Update the existing pairing after a successful provisioning run.
    /// Discovery has already stored the robot's identity via <see cref="SaveManualPairing"/>
    /// before navigating to WiFi setup, so only the network-dependent fields change here.
    /// No-op if nothing is paired.
    /// </summary>
    public void UpdateAfterProvisioning(string targetNetworkSsid, string wlan0Ip, string dashboardUrl)
    {
        var robot = GetPairedRobot();
        if (robot is null) return;

        robot.TargetNetworkSsid = targetNetworkSsid ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(wlan0Ip))      robot.Wlan0Ip      = wlan0Ip;
        if (!string.IsNullOrWhiteSpace(dashboardUrl)) robot.DashboardUrl = dashboardUrl;
        robot.PairedAt = DateTime.UtcNow;

        _settings.Set(PrefsKey, JsonSerializer.Serialize(robot, _json));
    }

    /// <summary>
    /// Save pairing for manually entered hostname or IP.
    /// </summary>
    public void SaveManualPairing(RobotInfo info, string hostOrIp)
    {
        var isIp = System.Net.IPAddress.TryParse(hostOrIp, out _);
        var robot = new PairedRobot
        {
            Serial            = info.Serial,
            Name              = info.Name,
            Model             = info.Model,
            FirmwareVersion   = info.FirmwareVersion,
            MdnsHostname      = info.MdnsHostname,
            Wlan0Ip           = isIp ? hostOrIp : string.Empty,
            DashboardUrl      = $"http://{hostOrIp}:8080",
            PairedAt          = DateTime.UtcNow,
        };
        _settings.Set(PrefsKey, JsonSerializer.Serialize(robot, _json));
    }

    // ── Delete ──────────────────────────────────────────────────────────────

    /// <summary>Clear pairing data. Called by "Forget Robot" in Settings tab.</summary>
    public void ClearPairedRobot() => _settings.Remove(PrefsKey);

    // ── Helpers ─────────────────────────────────────────────────────────────

    public bool HasPairedRobot() => GetPairedRobot() is not null;

    /// <summary>
    /// Returns base URL using mDNS hostname, or wlan0 IP as fallback if specified.
    /// </summary>
    public string? GetPairedBaseUrl(bool useIpFallback = false)
    {
        var robot = GetPairedRobot();
        if (robot is null) return null;

        if (useIpFallback && !string.IsNullOrWhiteSpace(robot.Wlan0Ip))
            return $"http://{robot.Wlan0Ip}:8000";

        var host = string.IsNullOrWhiteSpace(robot.MdnsHostname) ? robot.Wlan0Ip : robot.MdnsHostname;
        return string.IsNullOrWhiteSpace(host) ? null : $"http://{host}:8000";
    }
}
